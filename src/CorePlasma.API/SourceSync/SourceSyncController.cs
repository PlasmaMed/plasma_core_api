using System.Security.Cryptography;
using System.Text;
using CorePlasma.API.Authorization;
using CorePlasma.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CorePlasma.API.SourceSync;

[ApiController]
[Route("api/integration/source-sync/{source}")]
[Authorize(Policy = AuthorizationPolicies.ApiKey)]
[RequestSizeLimit(SourceSyncCodec.MaximumBodyBytes)]
public sealed class SourceSyncController(SourceSyncStore store, IOptionsMonitor<SourceSyncOptions> options) : ControllerBase
{
    [HttpPost("snapshot/begin")]
    public async Task<IActionResult> Begin(string source, SourceSyncBegin body, CancellationToken cancellationToken)
    {
        Authenticate(source, body.InstallationId);
        return Ok(await store.BeginAsync(source, body, cancellationToken));
    }

    [HttpPost("snapshot/rows")]
    public async Task<IActionResult> Rows(string source, SourceSyncRows body, CancellationToken cancellationToken)
    {
        Authenticate(source, body.InstallationId);
        return Ok(await store.RowsAsync(source, body, cancellationToken));
    }

    [HttpPost("snapshot/complete")]
    public async Task<IActionResult> Complete(string source, SourceSyncComplete body, CancellationToken cancellationToken)
    {
        Authenticate(source, body.InstallationId);
        var result = await store.CompleteAsync(source, body, cancellationToken);
        return StatusCode(result.Status == "ready" ? StatusCodes.Status200OK : StatusCodes.Status202Accepted, result);
    }

    [HttpPost("events")]
    public async Task<IActionResult> Events(string source, SourceSyncEvents body, CancellationToken cancellationToken)
    {
        Authenticate(source, body.InstallationId);
        return Ok(await store.EventsAsync(source, body, cancellationToken));
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(string source, CancellationToken cancellationToken)
    {
        Authenticate(source, null);
        var state = await store.GetStateAsync(source, cancellationToken);
        return Ok(state is null ? new { source, installationId = (Guid?)null, generation = (Guid?)null,
            status = "unbound", tableCount = 0, pendingProjectionCount = 0L, projectionState = "pending" } : (object)state);
    }

    [HttpPost("events/parts")]
    public async Task<IActionResult> StagePart(string source, SourceSyncEventPart body, CancellationToken cancellationToken)
    {
        Authenticate(source, body.InstallationId);
        return Ok(await store.StagePartAsync(source, body, cancellationToken));
    }

    [HttpPost("events/commit")]
    public async Task<IActionResult> CommitEvents(string source, SourceSyncEventCommit body, CancellationToken cancellationToken)
    {
        Authenticate(source, body.InstallationId);
        return StatusCode(StatusCodes.Status202Accepted, await store.CommitEventsAsync(source, body, cancellationToken));
    }

    [HttpGet("events/{batchId:guid}")]
    public async Task<IActionResult> EventStatus(string source, Guid batchId, CancellationToken cancellationToken)
    {
        Authenticate(source, null);
        return Ok(await store.GetEventJobAsync(source, batchId, null, cancellationToken));
    }

    [HttpGet("events/{batchId:guid}/parts/{partIndex:int}")]
    public async Task<IActionResult> EventPartStatus(string source, Guid batchId, int partIndex, CancellationToken cancellationToken)
    {
        Authenticate(source, null);
        return Ok(await store.GetEventJobAsync(source, batchId, partIndex, cancellationToken));
    }

    private void Authenticate(string source, Guid? installation)
    {
        SourceSyncCodec.Schema(source);
        var settings = options.CurrentValue;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ConnectionString)
            || !settings.Sources.TryGetValue(source, out var configured) || configured.InstallationId == Guid.Empty)
            throw new ForbiddenException("Source sync is not configured.");
        var supplied = Request.Headers["X-Source-Sync-Key"];
        var expected = configured.Key;
        if (expected is null || expected.Length is < 32 or > 512 || supplied.Count != 1 || supplied[0] is not { } actual
            || actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected)))
            throw new ForbiddenException("Source sync authentication failed.");
        if (installation is { } id && id != configured.InstallationId)
            throw new ConflictException("Source installation does not match the configured binding.");
    }
}
