using CorePlasma.API.Authentication;
using CorePlasma.API.Authorization;
using CorePlasma.API.Extensions;
using CorePlasma.Common.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

const string key = "cp_smoke_test_012345678901234567890123456789";
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test", ["Jwt:Secret"] = key,
    ["ApiKey:Key"] = key
}).Build();
var services = new ServiceCollection();
services.AddLogging();
services.AddApplicationServices(configuration);
using var provider = services.BuildServiceProvider();

async Task<AuthenticateResult> Authenticate(string[]? values, string scheme = ApiKeyOptions.SchemeName)
{
    using var scope = provider.CreateScope();
    var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
    if (values != null) context.Request.Headers[ApiKeyOptions.HeaderName] = new StringValues(values);
    return await context.AuthenticateAsync(scheme);
}
void Check(bool success, string label)
{
    if (!success) throw new InvalidOperationException(label);
    Console.WriteLine($"PASS: {label}");
}

var valid = await Authenticate([key]);
Check(valid.Succeeded, "Correct key authenticates");
Check(valid.Principal!.FindFirst(AppClaimTypes.EmployeeId) == null &&
      valid.Principal.FindFirst(AppClaimTypes.CustomerId) == null, "Service identity has no employee/customer privileges");
Check((await Authenticate(null)).None, "Missing key is not authenticated");
Check((await Authenticate(["wrong"])).Failure != null, "Wrong key is rejected");
Check((await Authenticate([""])).Failure != null, "Empty key is rejected");
Check((await Authenticate([key, key])).Failure != null, "Duplicate headers are rejected");
Check((await Authenticate([new string('x', 513)])).Failure != null, "Oversized key is rejected");
Check((await Authenticate([key], JwtBearerDefaults.AuthenticationScheme)).None,
    "API key does not authenticate JWT endpoints");
var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();
var integration = await policies.GetPolicyAsync(AuthorizationPolicies.ApiKey);
Check(integration!.AuthenticationSchemes.SequenceEqual([ApiKeyOptions.SchemeName]),
    "Integration policy explicitly requires API key scheme");
var schemes = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync();
Check(schemes!.Name == JwtBearerDefaults.AuthenticationScheme, "Default authentication stays JWT");
provider.GetRequiredService<IOptionsMonitor<ApiKeyOptions>>().Get(ApiKeyOptions.SchemeName).Key = "";
Check((await Authenticate([key])).Failure != null, "Unconfigured key fails closed");

using (var scope = provider.CreateScope())
{
    var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
    context.Response.Body = new MemoryStream();
    await context.ChallengeAsync(ApiKeyOptions.SchemeName);
    Check(context.Response.StatusCode == 401 && context.Response.Headers.WWWAuthenticate == "ApiKey",
        "Missing/invalid credentials produce 401 challenge");
}
