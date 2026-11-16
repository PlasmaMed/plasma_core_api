using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CorePlasma.API.Authentication;
using CorePlasma.API.Controllers;
using CorePlasma.API.Extensions;
using CorePlasma.API.Integration;
using CorePlasma.BAL.DTOs.Customers;
using CorePlasma.BAL.DTOs.Engines;
using CorePlasma.BAL.DTOs.Tanks;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Models;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PlmIntegration = PlasmaVN.API.Integration;

// All credentials and records below are synthetic and exist only for this process.
// An empty builder does not run API Program or load appsettings, environment secrets or user-secrets.
var testKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var jwtSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var checks = new CheckSuite();

try
{
    await RunHttpConnectionsAsync(checks, testKey, jwtSecret);
    await RunPeerClientCasesAsync(checks, testKey);
    Console.WriteLine($"PASS: {checks.Count} integration connection checks; no database was used.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL: {exception.GetType().Name}: {exception.Message}");
    return 1;
}

static async Task RunHttpConnectionsAsync(CheckSuite checks, string testKey, string jwtSecret)
{
    await using var left = await TestHost.StartAsync(isCore: true, testKey, jwtSecret);
    await using var right = await TestHost.StartAsync(isCore: false, testKey, jwtSecret);
    left.PointAt(right.Address);
    right.PointAt(left.Address);

    var jwt = CreateEmployeeJwt(jwtSecret);
    string[] resources = ["engines", "tanks", "customers"];

    foreach (var host in new[] { left, right })
    {
        var recordId = host.IsCore ? "11111111-1111-4111-8111-111111111111" : "123";
        var endpoints = host.App.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("api/integration", StringComparison.Ordinal) == true)
            .ToArray();
        checks.That(endpoints.Length >= 11, $"Host {host.Marker}: actual integration controller routes are mapped");
        const string historyRoute = "api/integration/history/sessions";
        var historyEndpoints = endpoints.Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/') == historyRoute).ToArray();
        var readEndpoints = endpoints.Except(historyEndpoints).ToArray();
        checks.That(readEndpoints.Length >= 11 && readEndpoints.All(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.SequenceEqual(["GET"]) == true),
            $"Host {host.Marker}: integration read routes expose GET only");
        checks.That(historyEndpoints.Length == (host.IsCore ? 1 : 0) &&
                    historyEndpoints.All(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.SequenceEqual(["POST"]) == true),
            $"Host {host.Marker}: only the dedicated Core history endpoint allows integration POST");
        checks.That(historyEndpoints.All(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Any(policy => policy.Policy == CorePlasma.API.Authorization.AuthorizationPolicies.ApiKey)),
            $"Host {host.Marker}: the dedicated history POST retains the API-key authorization policy");

        using (var response = await host.SendAsync("api/integration/ping", keys: [testKey]))
        {
            checks.That(response.StatusCode == HttpStatusCode.OK, $"Host {host.Marker}: key-authenticated ping succeeds");
            var body = await ReadJsonAsync(response);
            checks.That(body.GetProperty("success").GetBoolean() &&
                        body.GetProperty("data").GetProperty("connected").GetBoolean(),
                $"Host {host.Marker}: ping returns the integration envelope");
        }

        var protectedPaths = new List<string> { "api/integration/ping", "api/integration/peer/ping" };
        foreach (var resource in resources)
        {
            protectedPaths.Add($"api/integration/{resource}");
            protectedPaths.Add($"api/integration/{resource}/{recordId}");
            protectedPaths.Add($"api/integration/peer/{resource}");
        }

        foreach (var path in protectedPaths)
        {
            foreach (var credentials in new (string Label, string[]? Keys)[]
                     { ("missing", null), ("wrong", ["incorrect-test-key"]), ("duplicate", [testKey, testKey]) })
            {
                using var response = await host.SendAsync(path, keys: credentials.Keys);
                checks.That(response.StatusCode == HttpStatusCode.Unauthorized &&
                            (!host.IsCore || response.Headers.WwwAuthenticate.Any(header => header.Scheme == ApiKeyOptions.SchemeName)),
                    $"Host {host.Marker}: {path} rejects {credentials.Label} API key with 401");
            }
        }

        foreach (var invalid in new[] { "", new string('x', 513) })
        {
            using var response = await host.SendAsync("api/integration/ping", keys: [invalid]);
            checks.That(response.StatusCode == HttpStatusCode.Unauthorized,
                $"Host {host.Marker}: empty or oversized API key is rejected");
        }

        foreach (var resource in resources)
        {
            using (var response = await host.SendAsync($"api/integration/{resource}?page=2&pageSize=7&keyword=A%26B", keys: [testKey]))
            {
                checks.That(response.StatusCode == HttpStatusCode.OK,
                    $"Host {host.Marker}: integration {resource} reads the registered service");
                var data = (await ReadJsonAsync(response)).GetProperty("data");
                checks.That(data.GetProperty("page").GetInt32() == 2 && data.GetProperty("pageSize").GetInt32() == 7 &&
                            data.GetProperty("items")[0].GetProperty(CodeProperty(host, resource)).GetString() == $"{host.Marker}-{resource}",
                    $"Host {host.Marker}: {resource} paging and stub payload survive HTTP serialization");
                checks.That(host.Trace.Reads.Any(read => read.Resource == resource && read.Page == 2 && read.PageSize == 7 && read.Keyword == "A&B"),
                    $"Host {host.Marker}: {resource} query values reach the service unchanged");
            }

            using (var response = await host.SendAsync($"api/integration/{resource}/{recordId}", keys: [testKey]))
            {
                checks.That(response.StatusCode == HttpStatusCode.OK,
                    $"Host {host.Marker}: integration {resource} detail reads the registered service");
                var data = (await ReadJsonAsync(response)).GetProperty("data");
                var correctId = host.IsCore ? data.GetProperty("id").GetGuid().ToString() == recordId
                    : data.GetProperty("id").GetInt64().ToString() == recordId;
                checks.That(correctId && data.GetProperty(CodeProperty(host, resource)).GetString() == $"{host.Marker}-{resource}",
                    $"Host {host.Marker}: {resource} detail ID and data are returned");
            }

            using (var response = await host.SendAsync(host.JwtPath(resource), keys: [testKey]))
                checks.That(response.StatusCode == HttpStatusCode.Unauthorized,
                    $"Host {host.Marker}: API key cannot authenticate {host.JwtRouteDescription} {resource} route");

            foreach (var invalidQuery in new[] { "page=0", "page=1000001", "pageSize=0", "pageSize=201", "keyword=" + new string('x', 201) })
            {
                foreach (var prefix in new[] { "api/integration/", "api/integration/peer/" })
                {
                    using var response = await host.SendAsync(prefix + resource + "?" + invalidQuery, keys: [testKey]);
                    checks.That(response.StatusCode == HttpStatusCode.BadRequest,
                        $"Host {host.Marker}: {prefix}{resource} rejects invalid integration query");
                }
            }

            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
            {
                var path = method == HttpMethod.Post ? $"api/integration/{resource}" : $"api/integration/{resource}/{recordId}";
                using var response = await host.SendAsync(path, method, [testKey], jwt);
                checks.That(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                    $"Host {host.Marker}: {method} has no integration {resource} write route");
            }
        }

        using (var response = await host.SendAsync("api/integration/ping", bearer: jwt))
            checks.That(response.StatusCode == HttpStatusCode.Unauthorized,
                $"Host {host.Marker}: valid employee JWT alone cannot authenticate integration routes");

        using (var response = await host.SendAsync(host.JwtPath("engines"), bearer: jwt))
            checks.That(response.StatusCode == HttpStatusCode.OK,
                $"Host {host.Marker}: {host.JwtRouteDescription} engine route accepts an authorized employee JWT");

        using (var response = await host.SendAsync("api/integration/ping", keys: [testKey], bearer: "invalid-test-token"))
            checks.That(response.StatusCode == HttpStatusCode.OK,
                $"Host {host.Marker}: the integration policy selects API key independently of JWT");

        host.SetKey("");
        using (var response = await host.SendAsync("api/integration/ping", keys: [testKey]))
            checks.That(response.StatusCode == HttpStatusCode.Unauthorized,
                $"Host {host.Marker}: disabled API key fails closed");
        host.SetKey(testKey);
    }

    foreach (var (source, target) in new[] { (left, right), (right, left) })
    {
        using (var response = await source.SendAsync("api/integration/peer/ping", keys: [testKey]))
        {
            checks.That(response.StatusCode == HttpStatusCode.OK,
                $"Host {source.Marker} to {target.Marker}: actual outgoing HTTP ping authenticates");
            var data = (await ReadJsonAsync(response)).GetProperty("data");
            checks.That(data.GetProperty("service").GetString() == (target.IsCore ? "CorePlasma" : "PLM") && data.GetProperty("connected").GetBoolean(),
                $"Host {source.Marker} to {target.Marker}: peer handshake is returned");
        }

        foreach (var resource in resources)
        {
            using var response = await source.SendAsync($"api/integration/peer/{resource}?page=3&pageSize=9&keyword=peer%26test", keys: [testKey]);
            checks.That(response.StatusCode == HttpStatusCode.OK,
                $"Host {source.Marker} to {target.Marker}: actual outgoing HTTP {resource} read succeeds");
            var data = (await ReadJsonAsync(response)).GetProperty("data");
            checks.That(data.GetProperty("items")[0].GetProperty(CodeProperty(target, resource)).GetString() == $"{target.Marker}-{resource}",
                $"Host {source.Marker} to {target.Marker}: response comes from the opposite host");
            checks.That(target.Trace.Reads.Any(read => read.Resource == resource && read.Page == 3 && read.PageSize == 9 && read.Keyword == "peer&test"),
                $"Host {source.Marker} to {target.Marker}: peer paging and escaped keyword reach the opposite service");
        }

        target.SetKey(new string('Z', 64));
        using (var response = await source.SendAsync("api/integration/peer/ping", keys: [testKey]))
            checks.That(response.StatusCode == HttpStatusCode.BadGateway,
                $"Host {source.Marker} to {target.Marker}: mismatched peer key produces a safe 502");
        target.SetKey(testKey);

        source.PointAt(new Uri("http://public-peer.invalid/"));
        using (var response = await source.SendAsync("api/integration/peer/ping", keys: [testKey]))
            checks.That(response.StatusCode == HttpStatusCode.ServiceUnavailable,
                $"Host {source.Marker}: invalid peer configuration produces 503 before HTTP");
        source.PointAt(target.Address);
    }

    checks.That(left.Trace.WriteCount == 0 && right.Trace.WriteCount == 0, "HTTP connection suite performs no service writes");
}

static async Task RunPeerClientCasesAsync(CheckSuite checks, string testKey)
{
    const string validPing = "{\"success\":true,\"data\":{\"service\":\"TestPeer\",\"connected\":true,\"privateExtra\":\"omit-me\"}}";
    const string validList = "{\"success\":true,\"data\":{\"items\":[{\"id\":\"synthetic\"}],\"page\":2,\"pageSize\":200,\"totalItems\":1,\"totalPages\":1}}";

    using (var fixture = new PeerFixture(testKey, (_, _) => Task.FromResult(JsonResponse(validPing))))
    {
        var result = await fixture.Client.GetAsync("ping", new PagedRequest { Keyword = "ignored" }, CancellationToken.None);
        checks.That(result.StatusCode == 200 && result.Data!.Value.GetProperty("connected").GetBoolean(), "Peer client accepts a valid handshake");
        checks.That(result.Data.Value.EnumerateObject().Count() == 2 && !result.Data.Value.TryGetProperty("privateExtra", out _),
            "Peer client returns only handshake service and connected fields");
        checks.That(fixture.Handler.LastUri?.AbsoluteUri == "https://peer.invalid/prefix/api/integration/ping",
            "Peer client preserves configured base path and ignores ping query fields");
        checks.That(fixture.Handler.LastMethod == HttpMethod.Get && fixture.Handler.LastApiKeys.SequenceEqual([testKey]) &&
                    fixture.Handler.LastAccept == "application/json" && fixture.Handler.LastAuthorization is null,
            "Peer client sends one API key, JSON accept and GET without bearer authentication");
    }

    foreach (var resource in new[] { "engines", "tanks", "customers" })
    {
        using var fixture = new PeerFixture(testKey, (_, _) => Task.FromResult(JsonResponse(validList)));
        var query = new PagedRequest { Page = 2, PageSize = 999, Keyword = "A&B ?/Đ" };
        var result = await fixture.Client.GetAsync(resource, query, CancellationToken.None);
        checks.That(result.StatusCode == 200 && result.Data!.Value.GetProperty("items").GetArrayLength() == 1,
            $"Peer client accepts {resource} paged data");
        checks.That(fixture.Handler.LastUri?.AbsolutePath == $"/prefix/api/integration/{resource}" &&
                    fixture.Handler.LastUri.Query == "?page=2&pageSize=200&keyword=" + Uri.EscapeDataString(query.Keyword),
            $"Peer client {resource} path, paging cap and keyword encoding are correct");
    }

    using (var fixture = new PeerFixture(testKey, (_, _) => Task.FromResult(JsonResponse(validList))))
    {
        await checks.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Client.GetAsync("../auth", null, CancellationToken.None),
            "Peer client rejects resources outside the integration allowlist");
        checks.That(fixture.Handler.Calls == 0, "Disallowed resource does not send HTTP");
    }

    foreach (var config in new (string Label, string Url, string Key, string Environment)[]
             {
                 ("missing base URL", "", testKey, Environments.Development),
                 ("relative base URL", "peer/api", testKey, Environments.Development),
                 ("malformed base URL", "https://", testKey, Environments.Development),
                 ("public plaintext HTTP", "http://peer.invalid/", testKey, Environments.Development),
                 ("plaintext HTTP in production", "http://127.0.0.1:12345/", testKey, Environments.Production),
                 ("URL user info", "https://test:password@peer.invalid/", testKey, Environments.Development),
                 ("URL query", "https://peer.invalid/?query=1", testKey, Environments.Development),
                 ("URL fragment", "https://peer.invalid/#part", testKey, Environments.Development),
                 ("unsupported URL scheme", "ftp://peer.invalid/", testKey, Environments.Development),
                 ("missing key", "https://peer.invalid/", "", Environments.Development),
                 ("whitespace key", "https://peer.invalid/", "   ", Environments.Development),
                 ("key containing whitespace", "https://peer.invalid/", new string('x', 32) + " ", Environments.Development),
                 ("key containing a comma", "https://peer.invalid/", new string('x', 32) + ",", Environments.Development),
                 ("short key", "https://peer.invalid/", "short-test-key", Environments.Development),
                 ("oversized key", "https://peer.invalid/", new string('x', 513), Environments.Development)
             })
    {
        using var fixture = new PeerFixture(config.Key, (_, _) => Task.FromResult(JsonResponse(validPing)), config.Url, config.Environment);
        var result = await fixture.Client.GetAsync("ping", null, CancellationToken.None);
        checks.That(result.StatusCode == 503 && fixture.Handler.Calls == 0,
            $"Peer client rejects {config.Label} before HTTP");
    }

    using (var fixture = new PeerFixture(testKey, (_, _) => Task.FromResult(JsonResponse(validPing)), "http://127.0.0.1:12345/", Environments.Development))
        checks.That((await fixture.Client.GetAsync("ping", null, CancellationToken.None)).StatusCode == 200,
            "Peer client allows loopback HTTP for Development connection tests");

    foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.InternalServerError, HttpStatusCode.Redirect })
    {
        using var fixture = new PeerFixture(testKey, (_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent("synthetic-private-upstream-error"),
            Headers = { Location = new Uri("https://elsewhere.invalid/") }
        }));
        var result = await fixture.Client.GetAsync("ping", null, CancellationToken.None);
        checks.That(result.StatusCode == 502 && result.Data is null &&
                    result.Message?.Contains("synthetic-private-upstream-error", StringComparison.Ordinal) == false,
            $"Peer client maps upstream {(int)status} to a safe 502 response");
    }

    foreach (var invalid in new (string Label, string Json)[]
             {
                 ("malformed JSON", "{not json"),
                 ("non-object envelope", "[]"),
                 ("missing success", "{\"data\":{\"service\":\"Peer\",\"connected\":true}}"),
                 ("false success", "{\"success\":false,\"data\":{\"service\":\"Peer\",\"connected\":true}}"),
                 ("string success", "{\"success\":\"true\",\"data\":{\"service\":\"Peer\",\"connected\":true}}"),
                 ("null data", "{\"success\":true,\"data\":null}"),
                 ("array data", "{\"success\":true,\"data\":[]}"),
                 ("missing connected", "{\"success\":true,\"data\":{\"service\":\"Peer\"}}"),
                 ("disconnected peer", "{\"success\":true,\"data\":{\"service\":\"Peer\",\"connected\":false}}"),
                 ("missing service", "{\"success\":true,\"data\":{\"connected\":true}}"),
                 ("blank service", "{\"success\":true,\"data\":{\"service\":\" \",\"connected\":true}}"),
                 ("oversized service name", JsonSerializer.Serialize(new { success = true, data = new { service = new string('x', 101), connected = true } })),
                 ("excessive JSON depth", "{\"success\":true,\"data\":{\"service\":\"Peer\",\"connected\":true,\"extra\":" + new string('[', 40) + "0" + new string(']', 40) + "}}")
             })
    {
        using var fixture = new PeerFixture(testKey, (_, _) => Task.FromResult(JsonResponse(invalid.Json)));
        checks.That((await fixture.Client.GetAsync("ping", null, CancellationToken.None)).StatusCode == 502,
            $"Peer client rejects {invalid.Label}");
    }

    foreach (var invalid in new (string Label, string Json)[]
             {
                 ("missing items", "{\"success\":true,\"data\":{}}"),
                 ("non-array items", "{\"success\":true,\"data\":{\"items\":{}}}"),
                 ("more than 200 items", JsonSerializer.Serialize(new { success = true, data = new { items = Enumerable.Range(0, 201).ToArray() } })),
                 ("zero page", "{\"success\":true,\"data\":{\"items\":[],\"page\":0,\"pageSize\":10,\"totalItems\":0,\"totalPages\":0}}"),
                 ("non-numeric page", "{\"success\":true,\"data\":{\"items\":[],\"page\":\"1\",\"pageSize\":10,\"totalItems\":0,\"totalPages\":0}}"),
                 ("oversized page size", "{\"success\":true,\"data\":{\"items\":[],\"page\":1,\"pageSize\":201,\"totalItems\":0,\"totalPages\":0}}"),
                 ("negative total", "{\"success\":true,\"data\":{\"items\":[],\"page\":1,\"pageSize\":10,\"totalItems\":-1,\"totalPages\":0}}"),
                 ("missing total pages", "{\"success\":true,\"data\":{\"items\":[],\"page\":1,\"pageSize\":10,\"totalItems\":0}}")
             })
    {
        using var fixture = new PeerFixture(testKey, (_, _) => Task.FromResult(JsonResponse(invalid.Json)));
        checks.That((await fixture.Client.GetAsync("engines", null, CancellationToken.None)).StatusCode == 502,
            $"Peer client rejects list response with {invalid.Label}");
    }

    var tooLarge = new byte[4 * 1024 * 1024 + 1];
    foreach (var declaredLength in new[] { true, false })
    {
        using var fixture = new PeerFixture(testKey, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = declaredLength ? new ByteArrayContent(tooLarge) : new UndeclaredLengthContent(tooLarge)
        }));
        checks.That((await fixture.Client.GetAsync("ping", null, CancellationToken.None)).StatusCode == 502,
            $"Peer client enforces response size limit for {(declaredLength ? "declared" : "streamed")} content");
    }

    foreach (var exception in new Exception[] { new HttpRequestException("synthetic connection failure"), new IOException("synthetic stream failure") })
    {
        using var fixture = new PeerFixture(testKey, (_, _) => Task.FromException<HttpResponseMessage>(exception));
        checks.That((await fixture.Client.GetAsync("ping", null, CancellationToken.None)).StatusCode == 502,
            $"Peer client maps {exception.GetType().Name} to 502");
    }

    using (var fixture = new PeerFixture(testKey, (_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("synthetic timeout"))))
        checks.That((await fixture.Client.GetAsync("ping", null, CancellationToken.None)).StatusCode == 504,
            "Peer client maps transport timeout to 504");

    using (var fixture = new PeerFixture(testKey, (_, token) =>
           {
               token.ThrowIfCancellationRequested();
               return Task.FromResult(JsonResponse(validPing));
           }))
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await checks.ThrowsAsync<OperationCanceledException>(() => fixture.Client.GetAsync("ping", null, cancellation.Token),
            "Peer client preserves caller cancellation instead of returning a gateway error");
    }
}

static string CodeProperty(TestHost host, string resource) => (host.IsCore, resource) switch
{
    (true, "engines") => "engineCode", (true, "tanks") => "tankCode", (true, "customers") => "customerCode",
    (false, "engines" or "tanks") => "serial", (false, "customers") => "name",
    _ => throw new ArgumentOutOfRangeException(nameof(resource))
};

static string CreateEmployeeJwt(string secret)
{
    var token = new JwtSecurityToken("connection-tests", "connection-tests",
        [new Claim(AppClaimTypes.UserType, "Employee"), new Claim(AppClaimTypes.EmployeeId, Guid.NewGuid().ToString()),
         new Claim(AppClaimTypes.UserId, Guid.NewGuid().ToString()), new Claim(AppClaimTypes.Username, "synthetic-employee")],
        expires: DateTime.UtcNow.AddMinutes(5),
        signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256));
    return new JwtSecurityTokenHandler().WriteToken(token);
}

static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
{
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return document.RootElement.Clone();
}

static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
{
    Content = new StringContent(json, Encoding.UTF8, "application/json")
};

sealed class CheckSuite
{
    public int Count { get; private set; }

    public void That(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Count++;
        Console.WriteLine($"PASS: {label}");
    }

    public async Task ThrowsAsync<TException>(Func<Task> action, string label) where TException : Exception
    {
        try { await action(); }
        catch (TException) { That(true, label); return; }
        throw new InvalidOperationException(label);
    }
}

sealed class TestHost(WebApplication app, HttpClient http, bool isCore, StubTrace trace) : IAsyncDisposable
{
    public WebApplication App { get; } = app;
    public bool IsCore { get; } = isCore;
    public string Marker => IsCore ? "Core" : "PLM";
    public string JwtRouteDescription => IsCore ? "original Core JWT" : "representative PLM JWT boundary";
    public StubTrace Trace { get; } = trace;
    public Uri Address => http.BaseAddress!;

    public static async Task<TestHost> StartAsync(bool isCore, string apiKey, string jwtSecret)
    {
        var controllerAssembly = isCore ? typeof(IntegrationController).Assembly
            : typeof(PlasmaVN.API.Controllers.IntegrationController).Assembly;
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ApplicationName = controllerAssembly.GetName().Name,
            Args = []
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "connection-tests", ["Jwt:Audience"] = "connection-tests", ["Jwt:Secret"] = jwtSecret,
            ["Jwt:ExpirationMinutes"] = "5", ["Jwt:RememberMeDays"] = "1", ["ApiKey:Key"] = apiKey,
            ["IntegrationPeer:BaseUrl"] = ""
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var trace = new StubTrace();
        if (isCore)
        {
            builder.Services.AddApplicationServices(builder.Configuration);
            builder.Services.AddSingleton<IEngineService>(new EngineStub("Core", trace));
            builder.Services.AddSingleton<ITankService>(new TankStub("Core", trace));
            builder.Services.AddSingleton<ICustomerService>(new CustomerStub("Core", trace));
            builder.Services.AddSingleton(PermissionStub.Create());
            builder.Services.AddSingleton(ThrowingProxy.Create<IEquipmentImportService>());
        }
        else
        {
            // PLM production registration includes EF; register its actual integration types only.
            builder.Services.Configure<PlmIntegration.ApiKeyOptions>(builder.Configuration.GetSection("ApiKey"));
            builder.Services.Configure<PlmIntegration.IntegrationPeerOptions>(builder.Configuration.GetSection("IntegrationPeer"));
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = "connection-tests",
                    ValidateAudience = true, ValidAudience = "connection-tests",
                    ValidateLifetime = true, ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
                })
                .AddScheme<AuthenticationSchemeOptions, PlmIntegration.ApiKeyAuthenticationHandler>(PlmIntegration.IntegrationAuthentication.Scheme, _ => { });
            builder.Services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
                options.AddPolicy(PlmIntegration.IntegrationAuthentication.Policy,
                    new AuthorizationPolicyBuilder(PlmIntegration.IntegrationAuthentication.Scheme).RequireAuthenticatedUser().Build());
            });
            builder.Services.AddHttpClient<PlmIntegration.IntegrationPeerClient>(client => client.Timeout = TimeSpan.FromSeconds(15))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
                .RedactLoggedHeaders([PlmIntegration.IntegrationAuthentication.Header]);
            builder.Services.AddSingleton<PlmIntegration.IIntegrationDataReader>(new PlmReadStub(trace));
        }
        builder.Services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            manager.ApplicationParts.Clear();
            manager.ApplicationParts.Add(new AssemblyPart(controllerAssembly));
        });
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        if (!isCore)
        {
            // Boundary probes represent legacy JWT-only routes without loading their DB-bound controllers.
            var jwtPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().Build();
            foreach (var route in new[] { "/Engine", "/Tank", "/Customer" })
                app.MapGet(route, () => Results.Ok(new { syntheticJwtBoundaryProbe = true })).RequireAuthorization(jwtPolicy);
        }
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        var http = new HttpClient { BaseAddress = new Uri(addresses.Single()), Timeout = TimeSpan.FromSeconds(15) };
        return new TestHost(app, http, isCore, trace);
    }

    public void PointAt(Uri peer)
    {
        if (IsCore) App.Services.GetRequiredService<IOptionsMonitor<IntegrationPeerOptions>>().CurrentValue.BaseUrl = peer.AbsoluteUri;
        else App.Services.GetRequiredService<IOptionsMonitor<PlmIntegration.IntegrationPeerOptions>>().CurrentValue.BaseUrl = peer.AbsoluteUri;
    }

    public void SetKey(string value)
    {
        if (IsCore) App.Services.GetRequiredService<IOptionsMonitor<ApiKeyOptions>>().Get(ApiKeyOptions.SchemeName).Key = value;
        else App.Services.GetRequiredService<IOptionsMonitor<PlmIntegration.ApiKeyOptions>>().CurrentValue.Key = value;
    }

    public string JwtPath(string resource) => IsCore ? $"api/{resource}" : resource switch
    {
        "engines" => "Engine", "tanks" => "Tank", "customers" => "Customer",
        _ => throw new ArgumentOutOfRangeException(nameof(resource))
    };

    public async Task<HttpResponseMessage> SendAsync(string path, HttpMethod? method = null, string[]? keys = null, string? bearer = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        if (keys is not null) request.Headers.TryAddWithoutValidation(ApiKeyOptions.HeaderName, keys);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await http.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        http.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }
}

sealed record StubRead(string Resource, int Page, int PageSize, string? Keyword);

sealed class StubTrace
{
    public ConcurrentQueue<StubRead> Reads { get; } = new();
    public int WriteCount;
}

abstract class CrudStub<TList, TDetail, TCreate, TUpdate, TFilter>(string resource, string marker, StubTrace trace)
    : ICrudService<TList, TDetail, TCreate, TUpdate, TFilter>
    where TDetail : TList
    where TFilter : PagedRequest
{
    protected abstract TDetail Record(Guid id, string code);

    public Task<PagedResult<TList>> GetPagedAsync(TFilter filter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        trace.Reads.Enqueue(new StubRead(resource, filter.Page, filter.PageSize, filter.Keyword));
        return Task.FromResult(PagedResult<TList>.Create(
            [Record(Guid.Parse("11111111-1111-4111-8111-111111111111"), $"{marker}-{resource}")], filter.Page, filter.PageSize, 1));
    }

    public Task<TDetail> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Record(id, $"{marker}-{resource}"));
    }

    public Task<TDetail> CreateAsync(TCreate dto, CancellationToken cancellationToken) => UnexpectedWrite<TDetail>();
    public Task<TDetail> UpdateAsync(Guid id, TUpdate dto, CancellationToken cancellationToken) => UnexpectedWrite<TDetail>();
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => UnexpectedWrite<object>();

    private Task<TResult> UnexpectedWrite<TResult>()
    {
        Interlocked.Increment(ref trace.WriteCount);
        throw new InvalidOperationException("A read-only connection test reached a write service method.");
    }
}

sealed class EngineStub(string marker, StubTrace trace)
    : CrudStub<EngineDto, EngineDetailDto, CreateEngineDto, UpdateEngineDto, EngineFilterDto>("engines", marker, trace), IEngineService
{
    protected override EngineDetailDto Record(Guid id, string code) => new() { Id = id, EngineCode = code, EngineName = "Synthetic engine" };
}

sealed class TankStub(string marker, StubTrace trace)
    : CrudStub<TankDto, TankDetailDto, CreateTankDto, UpdateTankDto, TankFilterDto>("tanks", marker, trace), ITankService
{
    protected override TankDetailDto Record(Guid id, string code) => new() { Id = id, TankCode = code };
}

sealed class CustomerStub(string marker, StubTrace trace)
    : CrudStub<CustomerDto, CustomerDetailDto, CreateCustomerDto, UpdateCustomerDto, CustomerFilterDto>("customers", marker, trace), ICustomerService
{
    protected override CustomerDetailDto Record(Guid id, string code) => new() { Id = id, CustomerCode = code, CustomerName = "Synthetic customer" };
}

sealed class PlmReadStub(StubTrace trace) : PlmIntegration.IIntegrationDataReader
{
    public Task<PlmIntegration.IntegrationPage<PlmIntegration.IntegrationEngineDto>> GetEnginesAsync(int page, int pageSize, string keyword, CancellationToken cancellationToken) =>
        Page("engines", new PlmIntegration.IntegrationEngineDto { Id = 123, Serial = "PLM-engines", Name = "Synthetic PLM engine" }, page, pageSize, keyword, cancellationToken);
    public Task<PlmIntegration.IntegrationPage<PlmIntegration.IntegrationTankDto>> GetTanksAsync(int page, int pageSize, string keyword, CancellationToken cancellationToken) =>
        Page("tanks", new PlmIntegration.IntegrationTankDto { Id = 123, Serial = "PLM-tanks", Rfid = "synthetic-rfid" }, page, pageSize, keyword, cancellationToken);
    public Task<PlmIntegration.IntegrationPage<PlmIntegration.IntegrationCustomerDto>> GetCustomersAsync(int page, int pageSize, string keyword, CancellationToken cancellationToken) =>
        Page("customers", new PlmIntegration.IntegrationCustomerDto { Id = 123, Name = "PLM-customers" }, page, pageSize, keyword, cancellationToken);
    public Task<PlmIntegration.IntegrationEngineDto> GetEngineAsync(long id, CancellationToken cancellationToken) =>
        Detail(new PlmIntegration.IntegrationEngineDto { Id = id, Serial = "PLM-engines", Name = "Synthetic PLM engine" }, cancellationToken);
    public Task<PlmIntegration.IntegrationTankDto> GetTankAsync(long id, CancellationToken cancellationToken) =>
        Detail(new PlmIntegration.IntegrationTankDto { Id = id, Serial = "PLM-tanks", Rfid = "synthetic-rfid" }, cancellationToken);
    public Task<PlmIntegration.IntegrationCustomerDto> GetCustomerAsync(long id, CancellationToken cancellationToken) =>
        Detail(new PlmIntegration.IntegrationCustomerDto { Id = id, Name = "PLM-customers" }, cancellationToken);

    private Task<PlmIntegration.IntegrationPage<T>> Page<T>(string resource, T item, int page, int pageSize, string keyword, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        trace.Reads.Enqueue(new StubRead(resource, page, pageSize, keyword));
        return Task.FromResult(new PlmIntegration.IntegrationPage<T>
        {
            Items = [item], Page = page, PageSize = pageSize, TotalItems = 1, TotalPages = 1
        });
    }

    private static Task<T> Detail<T>(T item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(item);
    }
}

public class PermissionStub : System.Reflection.DispatchProxy
{
    public static IPermissionService Create() => Create<IPermissionService, PermissionStub>();

    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? arguments) => method?.Name switch
    {
        nameof(IPermissionService.HasPermissionAsync) => Task.FromResult(true),
        nameof(IPermissionService.IsSuperAdminAsync) => Task.FromResult(false),
        _ => throw new InvalidOperationException("Connection test requested an unrelated permission service method.")
    };
}

public class ThrowingProxy : System.Reflection.DispatchProxy
{
    public static T Create<T>() where T : class => Create<T, ThrowingProxy>();

    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? arguments) =>
        throw new InvalidOperationException("Connection test requested an unrelated service method.");
}

sealed class PeerFixture : IDisposable
{
    private readonly HttpClient http;
    public RecordingHandler Handler { get; }
    public IntegrationPeerClient Client { get; }

    public PeerFixture(string key, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        string url = "https://peer.invalid/prefix/", string environment = "Development")
    {
        Handler = new RecordingHandler(respond);
        http = new HttpClient(Handler);
        Client = new IntegrationPeerClient(http,
            new ConstantOptions<IntegrationPeerOptions>(new() { BaseUrl = url }),
            new ConstantOptions<ApiKeyOptions>(new() { Key = key }),
            new TestEnvironment { EnvironmentName = environment });
    }

    public void Dispose() => http.Dispose();
}

sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public Uri? LastUri { get; private set; }
    public HttpMethod? LastMethod { get; private set; }
    public string[] LastApiKeys { get; private set; } = [];
    public string? LastAccept { get; private set; }
    public string? LastAuthorization { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        LastUri = request.RequestUri;
        LastMethod = request.Method;
        LastApiKeys = request.Headers.TryGetValues(ApiKeyOptions.HeaderName, out var keys) ? keys.ToArray() : [];
        LastAccept = request.Headers.Accept.ToString();
        LastAuthorization = request.Headers.Authorization?.ToString();
        return respond(request, cancellationToken);
    }
}

sealed class ConstantOptions<T>(T value) : IOptionsMonitor<T> where T : class
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "IntegrationConnectionTests";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}

sealed class UndeclaredLengthContent(byte[] bytes) : HttpContent
{
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
}
