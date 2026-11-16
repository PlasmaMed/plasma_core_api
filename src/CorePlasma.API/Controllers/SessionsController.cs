using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Sessions;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

/// <summary>Lịch sử phiên sử dụng máy và các bình khí dùng trong từng phiên.</summary>
[Route("api/sessions")]
[HasPermission(PermissionCodes.Engine.View)]
public sealed class SessionsController(ISessionService sessionService) : ApiControllerBase
{
    /// <summary>Newest first. Filter by engineId, engine serial, tank RFID and start-time range.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SessionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<SessionDto>>>> GetSessions(
        [FromQuery] SessionFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await sessionService.GetPagedAsync(filter, cancellationToken));

    /// <summary>Same filters as the list; downloads an .xlsx file (needs ENGINE_EXPORT).</summary>
    [HttpGet("export")]
    [HasPermission(PermissionCodes.Engine.Export)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportSessions([FromQuery] SessionFilterDto filter, CancellationToken cancellationToken) =>
        File(await sessionService.ExportAsync(filter, cancellationToken),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"lich-su-su-dung-{DateTime.UtcNow.AddHours(7):yyyyMMdd-HHmm}.xlsx");

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SessionDto>>> GetSession(Guid id, CancellationToken cancellationToken) =>
        Success(await sessionService.GetByIdAsync(id, cancellationToken));

    /// <summary>Tank–engine RFID events (newest first). Filter by engineId, serial, tankRfid, eventType, time range.</summary>
    [HttpGet("~/api/tank-engine-histories")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TankEngineHistoryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<TankEngineHistoryDto>>>> GetTankEngineHistories(
        [FromQuery] TankEngineHistoryFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await sessionService.GetTankEngineHistoryAsync(filter, cancellationToken));
}
