using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Integration;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

[Route("api/integration/history")]
[Authorize(Policy = AuthorizationPolicies.ApiKey)]
public sealed class IntegrationHistoryController(IHistoryIngestService historyIngestService) : ApiControllerBase
{
    /// <summary>Atomically accept a committed NEWAPP history snapshot; retries are idempotent.</summary>
    [HttpPost("sessions")]
    [RequestSizeLimit(1_048_576)]
    [ProducesResponseType(typeof(ApiResponse<HistoryIngestResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IngestSession([FromBody] HistorySessionIngestDto dto, CancellationToken cancellationToken) =>
        Success(await historyIngestService.IngestAsync(dto, cancellationToken));
}
