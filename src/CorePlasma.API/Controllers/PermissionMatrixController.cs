using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Permissions;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

[Route("api/permission-matrix")]
public sealed class PermissionMatrixController(IPermissionMatrixService permissionMatrixService) : ApiControllerBase
{
    /// <summary>
    /// One page of the Employee × Permission checkbox matrix. Filter by employee, department, module,
    /// function, permission. Only the visible page of cells is loaded.
    /// </summary>
    [HttpGet]
    [HasPermission(PermissionCodes.Permission.View)]
    [ProducesResponseType(typeof(ApiResponse<PermissionMatrixDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PermissionMatrixDto>>> GetMatrix(
        [FromQuery] PermissionMatrixFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await permissionMatrixService.GetAsync(filter, cancellationToken));

    /// <summary>Bulk grant/revoke of checkbox changes in a single transaction.</summary>
    [HttpPut]
    [HasPermission(PermissionCodes.Permission.Grant, PermissionCodes.Permission.Revoke)]
    [ProducesResponseType(typeof(ApiResponse<PermissionChangeResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PermissionChangeResultDto>>> UpdateMatrix(
        [FromBody] UpdatePermissionMatrixDto dto,
        CancellationToken cancellationToken) =>
        Success(await permissionMatrixService.UpdateAsync(dto, cancellationToken));
}
