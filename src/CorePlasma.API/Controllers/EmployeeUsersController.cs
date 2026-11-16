using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Users;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

/// <summary>Login accounts of employees (table EmployeeUsers).</summary>
[Route("api/employee-users")]
public sealed class EmployeeUsersController(IEmployeeUserService employeeUserService) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.EmployeeUser.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<EmployeeUserDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<EmployeeUserDto>>>> GetEmployeeUsers(
        [FromQuery] EmployeeUserFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await employeeUserService.GetPagedAsync(filter, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.EmployeeUser.View)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeUserDto>>> GetEmployeeUser(Guid id, CancellationToken cancellationToken) =>
        Success(await employeeUserService.GetByIdAsync(id, cancellationToken));

    /// <summary>Create a login account for an existing employee.</summary>
    [HttpPost]
    [HasPermission(PermissionCodes.EmployeeUser.Create)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeUserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EmployeeUserDto>>> CreateEmployeeUser(
        [FromBody] CreateEmployeeUserDto dto,
        CancellationToken cancellationToken)
    {
        var result = await employeeUserService.CreateAsync(dto, cancellationToken);
        return Created(nameof(GetEmployeeUser), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.EmployeeUser.Update)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EmployeeUserDto>>> UpdateEmployeeUser(
        Guid id,
        [FromBody] UpdateEmployeeUserDto dto,
        CancellationToken cancellationToken) =>
        Success(await employeeUserService.UpdateAsync(id, dto, cancellationToken));

    /// <summary>Administrator resets the account password.</summary>
    [HttpPut("{id:guid}/password")]
    [HasPermission(PermissionCodes.EmployeeUser.Update)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> ResetPassword(
        Guid id,
        [FromBody] ResetPasswordDto dto,
        CancellationToken cancellationToken)
    {
        await employeeUserService.ResetPasswordAsync(id, dto, cancellationToken);
        return Success("Password reset");
    }

    [HttpPost("{id:guid}/lock")]
    [HasPermission(PermissionCodes.EmployeeUser.Lock)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Lock(Guid id, CancellationToken cancellationToken)
    {
        await employeeUserService.LockAsync(id, cancellationToken);
        return Success("Locked");
    }

    [HttpPost("{id:guid}/unlock")]
    [HasPermission(PermissionCodes.EmployeeUser.Unlock)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Unlock(Guid id, CancellationToken cancellationToken)
    {
        await employeeUserService.UnlockAsync(id, cancellationToken);
        return Success("Unlocked");
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.EmployeeUser.Delete)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteEmployeeUser(Guid id, CancellationToken cancellationToken)
    {
        await employeeUserService.DeleteAsync(id, cancellationToken);
        return Success("Deleted");
    }
}
