using System.Reflection;
using CorePlasma.API.Authentication;
using CorePlasma.API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CorePlasma.API.Extensions;

public static class SwaggerExtensions
{
    private const string SchemeName = "Bearer";

    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Core Plasma API",
                Version = "v1",
                Description = "Core backend: Customer, Employee, Tank, Engine and permission matrix."
            });

            options.AddSecurityDefinition(SchemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the access token from POST /api/auth/login (without the 'Bearer ' prefix)."
            });

            options.AddSecurityDefinition(ApiKeyOptions.SchemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                Name = ApiKeyOptions.HeaderName,
                In = ParameterLocation.Header,
                Description = "Service-to-service API key for integration endpoints."
            });
            options.OperationFilter<EndpointSecurityOperationFilter>();

            var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xmlFile))
            {
                options.IncludeXmlComments(xmlFile);
            }
        });

        return services;
    }

    public static WebApplication UseSwaggerDocumentation(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Core Plasma API v1");
            options.DisplayRequestDuration();
        });

        return app;
    }
}

public sealed class EndpointSecurityOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
            return;

        var isApiKey = metadata.OfType<IAuthorizeData>()
            .Any(data => data.Policy == AuthorizationPolicies.ApiKey);
        operation.Security = [new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(isApiKey ? ApiKeyOptions.SchemeName : "Bearer",
                context.Document)] = []
        }];
    }
}
