using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.DataChangeLogs;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

/// <summary>Automatic history of every data change (who, when, old → new values).</summary>
[Route("api/data-change-logs")]
public sealed class DataChangeLogsController(IDataChangeLogService dataChangeLogService) : ApiControllerBase
{
    /// <summary>Newest first. Filter by table (entityName), record (entityId), action, user or date range.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.AuditLog.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<DataChangeLogDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<DataChangeLogDto>>>> GetDataChangeLogs(
        [FromQuery] DataChangeLogFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await dataChangeLogService.GetListAsync(filter, cancellationToken));
}
