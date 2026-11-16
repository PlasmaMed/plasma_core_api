using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CorePlasma.API.Authentication;

public sealed class ApiKeyOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";
    public string Key { get; set; } = string.Empty;

    public static bool IsValidKey(string? key) => key is { Length: >= 32 and <= 512 } &&
        key.All(c => c >= '!' && c <= '~' && c != ',');
}

/// <summary>Service credentials; enabled only on endpoints using the ApiKey policy.</summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<ApiKeyOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyOptions.HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!ApiKeyOptions.IsValidKey(Options.Key) || values.Count != 1 ||
            string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > 512)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(Options.Key));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(values[0]!));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "service-integration"),
             new Claim(ClaimTypes.Name, "service-integration")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = ApiKeyOptions.SchemeName;
        return Response.WriteAsJsonAsync(ApiResponse.Fail("Unauthorized"));
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(ApiResponse.Fail("You do not have permission to perform this action."));
    }
}
