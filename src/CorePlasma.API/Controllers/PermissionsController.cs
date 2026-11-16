using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Permissions;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Models;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

[Route("api/permissions")]
[HasPermission(PermissionCodes.Permission.View)]
public sealed class PermissionsController(IPermissionService permissionService) : ApiControllerBase
{
    /// <summary>Permission catalog; filter by moduleCode, functionCode, actionCode, keyword.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PermissionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PermissionDto>>>> GetPermissions(
        [FromQuery] PermissionFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await permissionService.GetListAsync(filter, cancellationToken));

    /// <summary>Employees currently holding the permission (paged).</summary>
    [HttpGet("{permissionId:guid}/employees")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PermissionEmployeeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedResult<PermissionEmployeeDto>>>> GetEmployees(
        Guid permissionId,
        [FromQuery] PagedRequest request,
        CancellationToken cancellationToken) =>
        Success(await permissionService.GetEmployeesAsync(permissionId, request, cancellationToken));

    /// <summary>Who granted/revoked which permission for which employee, and when.</summary>
    [HttpGet("audit-logs")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PermissionAuditLogDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<PermissionAuditLogDto>>>> GetAuditLogs(
        [FromQuery] PermissionAuditLogFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await permissionService.GetAuditLogsAsync(filter, cancellationToken));
}
