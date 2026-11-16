using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Users;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

/// <summary>Login accounts of customers (table CustomerUsers); filter by customerId.</summary>
[Route("api/customer-users")]
public sealed class CustomerUsersController(ICustomerUserService customerUserService) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.CustomerUser.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CustomerUserDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<CustomerUserDto>>>> GetCustomerUsers(
        [FromQuery] CustomerUserFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await customerUserService.GetPagedAsync(filter, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.CustomerUser.View)]
    [ProducesResponseType(typeof(ApiResponse<CustomerUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CustomerUserDto>>> GetCustomerUser(Guid id, CancellationToken cancellationToken) =>
        Success(await customerUserService.GetByIdAsync(id, cancellationToken));

    /// <summary>Create a login account for a contact of an existing customer.</summary>
    [HttpPost]
    [HasPermission(PermissionCodes.CustomerUser.Create)]
    [ProducesResponseType(typeof(ApiResponse<CustomerUserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CustomerUserDto>>> CreateCustomerUser(
        [FromBody] CreateCustomerUserDto dto,
        CancellationToken cancellationToken)
    {
        var result = await customerUserService.CreateAsync(dto, cancellationToken);
        return Created(nameof(GetCustomerUser), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.CustomerUser.Update)]
    [ProducesResponseType(typeof(ApiResponse<CustomerUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CustomerUserDto>>> UpdateCustomerUser(
        Guid id,
        [FromBody] UpdateCustomerUserDto dto,
        CancellationToken cancellationToken) =>
        Success(await customerUserService.UpdateAsync(id, dto, cancellationToken));

    /// <summary>Administrator resets the account password.</summary>
    [HttpPut("{id:guid}/password")]
    [HasPermission(PermissionCodes.CustomerUser.Update)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> ResetPassword(
        Guid id,
        [FromBody] ResetPasswordDto dto,
        CancellationToken cancellationToken)
    {
        await customerUserService.ResetPasswordAsync(id, dto, cancellationToken);
        return Success("Password reset");
    }

    [HttpPost("{id:guid}/lock")]
    [HasPermission(PermissionCodes.CustomerUser.Lock)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Lock(Guid id, CancellationToken cancellationToken)
    {
        await customerUserService.LockAsync(id, cancellationToken);
        return Success("Locked");
    }

    [HttpPost("{id:guid}/unlock")]
    [HasPermission(PermissionCodes.CustomerUser.Unlock)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Unlock(Guid id, CancellationToken cancellationToken)
    {
        await customerUserService.UnlockAsync(id, cancellationToken);
        return Success("Unlocked");
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.CustomerUser.Delete)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteCustomerUser(Guid id, CancellationToken cancellationToken)
    {
        await customerUserService.DeleteAsync(id, cancellationToken);
        return Success("Deleted");
    }
}
