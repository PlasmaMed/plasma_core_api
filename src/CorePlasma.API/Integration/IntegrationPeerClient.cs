using System.Text.Json;
using CorePlasma.API.Authentication;
using CorePlasma.Common.Models;
using Microsoft.Extensions.Options;

namespace CorePlasma.API.Integration;

public sealed class IntegrationPeerOptions
{
    public string BaseUrl { get; set; } = string.Empty;
}

public sealed record IntegrationPeerResult(int StatusCode, JsonElement? Data = null, string? Message = null);

/// <summary>Calls only the peer's dedicated, read-only integration routes.</summary>
public sealed class IntegrationPeerClient(
    HttpClient httpClient,
    IOptionsMonitor<IntegrationPeerOptions> peerOptions,
    IOptionsMonitor<ApiKeyOptions> keyOptions,
    IHostEnvironment environment)
{
    private const int MaxResponseBytes = 4 * 1024 * 1024;

    public async Task<IntegrationPeerResult> GetAsync(
        string resource, PagedRequest? query, CancellationToken cancellationToken)
    {
        if (resource is not ("ping" or "engines" or "tanks" or "customers"))
            throw new ArgumentOutOfRangeException(nameof(resource));

        var key = keyOptions.Get(ApiKeyOptions.SchemeName).Key;
        if (!ApiKeyOptions.IsValidKey(key) ||
            !TryGetBaseUri(peerOptions.CurrentValue.BaseUrl, out var baseUri))
            return new(StatusCodes.Status503ServiceUnavailable, Message: "The integration peer is not configured.");

        var path = "api/integration/" + resource;
        if (resource != "ping" && query is not null)
        {
            path += $"?page={query.Page}&pageSize={query.PageSize}";
            if (!string.IsNullOrWhiteSpace(query.Keyword))
                path += "&keyword=" + Uri.EscapeDataString(query.Keyword);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri!, path));
        request.Headers.Add(ApiKeyOptions.HeaderName, key);
        request.Headers.Accept.ParseAdd("application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return Failure();

            if (response.Content.Headers.ContentLength > MaxResponseBytes)
                return Failure();

            // Limit both declared and streamed bodies, including chunked responses.
            using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await body.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > MaxResponseBytes)
                    return Failure();
                buffer.Write(chunk, 0, read);
            }

            timeout.Token.ThrowIfCancellationRequested();
            using var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            var envelope = json.RootElement;
            if (envelope.ValueKind != JsonValueKind.Object ||
                !envelope.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
                !envelope.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return Failure();

            if (resource == "ping")
            {
                if (!data.TryGetProperty("connected", out var connected) || connected.ValueKind != JsonValueKind.True ||
                    !data.TryGetProperty("service", out var service) || service.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(service.GetString()) || service.GetString()!.Length > 100)
                    return Failure();

                // Do not return additional peer fields from the authentication handshake.
                return new(StatusCodes.Status200OK,
                    JsonSerializer.SerializeToElement(new { service = service.GetString(), connected = true }));
            }

            if (!data.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array ||
                items.GetArrayLength() > (query?.PageSize ?? PagedRequest.MaxPageSize) ||
                !data.TryGetProperty("page", out var page) || page.ValueKind != JsonValueKind.Number ||
                !page.TryGetInt32(out var returnedPage) || returnedPage < 1 ||
                !data.TryGetProperty("pageSize", out var size) || size.ValueKind != JsonValueKind.Number ||
                !size.TryGetInt32(out var returnedSize) || returnedSize is < 1 or > PagedRequest.MaxPageSize ||
                !data.TryGetProperty("totalItems", out var total) || total.ValueKind != JsonValueKind.Number ||
                !total.TryGetInt64(out var returnedTotal) || returnedTotal < 0 ||
                !data.TryGetProperty("totalPages", out var pages) || pages.ValueKind != JsonValueKind.Number ||
                !pages.TryGetInt64(out var returnedPages) || returnedPages < 0)
                return Failure();

            return new(StatusCodes.Status200OK, data.Clone());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(StatusCodes.Status504GatewayTimeout, Message: "The integration peer did not respond in time.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            return Failure();
        }
    }

    private bool TryGetBaseUri(string value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            !string.IsNullOrEmpty(candidate.UserInfo) || !string.IsNullOrEmpty(candidate.Query) ||
            !string.IsNullOrEmpty(candidate.Fragment) ||
            !(candidate.Scheme == Uri.UriSchemeHttps ||
              (candidate.Scheme == Uri.UriSchemeHttp && candidate.IsLoopback && environment.IsDevelopment())))
            return false;

        uri = new Uri(candidate.AbsoluteUri.TrimEnd('/') + "/");
        return true;
    }

    private static IntegrationPeerResult Failure() =>
        new(StatusCodes.Status502BadGateway, Message: "The integration peer returned an invalid or unsuccessful response.");
}
