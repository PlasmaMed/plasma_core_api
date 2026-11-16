using System.Text;
using System.Text.Json.Serialization;
using CorePlasma.API.Authorization;
using CorePlasma.API.Authentication;
using CorePlasma.API.Integration;
using CorePlasma.API.Filters;
using CorePlasma.API.Middleware;
using CorePlasma.API.Services;
using CorePlasma.API.SourceSync;
using CorePlasma.API.SourceProjection;
using CorePlasma.API.SourceBusiness;
using CorePlasma.API.QuanlyAccounts;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Interfaces;
using CorePlasma.Common.Models;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CorePlasma.API.Extensions;

public static class ApplicationServiceCollectionExtensions
{
    public const string CorsPolicyName = "DefaultCors";

    /// <summary>
    /// API-layer services: controllers, validation, exception handling, JWT, authorization, Swagger, CORS.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddSourceSyncServices(configuration);
        services.AddSourceProjectionServices(configuration);
        services.AddQuanlyAccountImportServices(configuration);
        services.AddSourceBusinessServices();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        services.Configure<PermissionCacheSettings>(configuration.GetSection(PermissionCacheSettings.SectionName));
        services.Configure<LoginSecuritySettings>(configuration.GetSection(LoginSecuritySettings.SectionName));
        var legacySync = configuration.GetSection(LegacySyncSettings.SectionName);
        services.AddOptions<LegacySyncSettings>().Configure(options =>
        {
            // An explicitly empty matrix denies every stream; an absent matrix keeps the older allowlist behavior.
            if (legacySync.GetChildren().Any(x => x.Key.Equals(nameof(LegacySyncSettings.AllowedStreams), StringComparison.OrdinalIgnoreCase)))
                options.AllowedStreams = [];
            legacySync.Bind(options);
        }).Validate(options => options.IsValid(), "LegacySync contains invalid or duplicate source/entity policies.")
            .ValidateOnStart();

        services.AddControllers(options => options.Filters.Add<ValidationFilter>())
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                // Model binding errors (malformed JSON, invalid route Guid...) use the same envelope.
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(x => x.Value?.Errors.Count > 0)
                        .SelectMany(x => x.Value!.Errors.Select(e =>
                            $"{x.Key}: {(string.IsNullOrEmpty(e.ErrorMessage) ? "Invalid value" : e.ErrorMessage)}"))
                        .ToList();
                    return new BadRequestObjectResult(ApiResponse.Fail("Validation failed", errors));
                };
            });

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        services.AddJwtAuthentication(configuration);
        services.AddPermissionAuthorization();
        services.Configure<IntegrationPeerOptions>(configuration.GetSection("IntegrationPeer"));
        services.AddHttpClient<IntegrationPeerClient>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            .RedactLoggedHeaders([ApiKeyOptions.HeaderName]);
        services.AddSwaggerDocumentation();

        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
        {
            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()
                    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName);
            }
        }));

        return services;
    }

    private static void AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.Issuer), "Jwt:Issuer is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Audience), "Jwt:Audience is required.")
            .Validate(s => Encoding.UTF8.GetByteCount(s.Secret ?? string.Empty) >= 32,
                "Jwt:Secret must be configured (user-secrets / environment) and be at least 32 bytes.")
            .Validate(s => s.ExpirationMinutes > 0, "Jwt:ExpirationMinutes must be positive.")
            .Validate(s => s.RememberMeDays is > 0 and <= 365, "Jwt:RememberMeDays must be between 1 and 365.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddScheme<ApiKeyOptions, ApiKeyAuthenticationHandler>(ApiKeyOptions.SchemeName,
                options => configuration.GetSection("ApiKey").Bind(options));
        services.AddOptions<ApiKeyOptions>(ApiKeyOptions.SchemeName)
            .Validate(s => string.IsNullOrEmpty(s.Key) || ApiKeyOptions.IsValidKey(s.Key),
                "ApiKey:Key must contain 32 to 512 printable ASCII characters without spaces or commas when configured.")
            .ValidateOnStart();

        // Resolved lazily so design-time tools (dotnet ef) do not need the secret.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = AppClaimTypes.Username
                };
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(ApiResponse.Fail("Unauthorized"));
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(
                            ApiResponse.Fail("You do not have permission to perform this action."));
                    }
                };
            });
    }

    private static void AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddAuthorizationBuilder()
            // Every endpoint requires an authenticated caller unless marked [AllowAnonymous].
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AuthorizationPolicies.ApiKey, policy => policy
                .AddAuthenticationSchemes(ApiKeyOptions.SchemeName)
                .RequireAuthenticatedUser())
            // Customer-portal endpoints: only CustomerUsers accounts.
            .AddPolicy(AuthorizationPolicies.CustomerUser, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(AppClaimTypes.UserType, nameof(UserType.Customer))
                .RequireClaim(AppClaimTypes.CustomerId));
    }
}
