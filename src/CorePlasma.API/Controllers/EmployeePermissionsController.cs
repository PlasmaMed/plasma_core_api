using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Permissions;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

[Route("api/employees/{employeeId:guid}/permissions")]
public sealed class EmployeePermissionsController(IEmployeePermissionService employeePermissionService) : ApiControllerBase
{
    /// <summary>All permission assignments of the employee.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.Permission.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EmployeePermissionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeePermissionDto>>>> GetPermissions(
        Guid employeeId,
        CancellationToken cancellationToken) =>
        Success(await employeePermissionService.GetByEmployeeAsync(employeeId, cancellationToken));

    /// <summary>
    /// Replace the employee's full permission set. Only the delta is inserted/deleted, in one transaction, with audit log.
    /// </summary>
    [HttpPut]
    [HasPermission(PermissionCodes.Permission.Grant, PermissionCodes.Permission.Revoke)]
    [ProducesResponseType(typeof(ApiResponse<PermissionChangeResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PermissionChangeResultDto>>> ReplacePermissions(
        Guid employeeId,
        [FromBody] UpdateEmployeePermissionsDto dto,
        CancellationToken cancellationToken) =>
        Success(await employeePermissionService.ReplaceAsync(employeeId, dto, cancellationToken));

    /// <summary>Grant one or more permissions (optionally with an expiry date).</summary>
    [HttpPost]
    [HasPermission(PermissionCodes.Permission.Grant)]
    [ProducesResponseType(typeof(ApiResponse<PermissionChangeResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PermissionChangeResultDto>>> GrantPermissions(
        Guid employeeId,
        [FromBody] AddEmployeePermissionsDto dto,
        CancellationToken cancellationToken) =>
        Success(await employeePermissionService.GrantAsync(employeeId, dto, cancellationToken));

    /// <summary>Revoke a single permission.</summary>
    [HttpDelete("{permissionId:guid}")]
    [HasPermission(PermissionCodes.Permission.Revoke)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> RevokePermission(
        Guid employeeId,
        Guid permissionId,
        CancellationToken cancellationToken)
    {
        await employeePermissionService.RevokeAsync(employeeId, permissionId, cancellationToken);
        return Success("Revoked");
    }
}
