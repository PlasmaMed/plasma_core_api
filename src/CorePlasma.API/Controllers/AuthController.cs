using CorePlasma.BAL.DTOs.Auth;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ApiControllerBase
{
    /// <summary>Sign in with an employee account (table EmployeeUsers).</summary>
    [AllowAnonymous]
    [HttpPost("employee/login")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<LoginResponseDto>>> EmployeeLogin(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken) =>
        Success(await authService.EmployeeLoginAsync(request, cancellationToken));

    /// <summary>Sign in with a customer account (table CustomerUsers).</summary>
    [AllowAnonymous]
    [HttpPost("customer/login")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<LoginResponseDto>>> CustomerLogin(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken) =>
        Success(await authService.CustomerLoginAsync(request, cancellationToken));

    /// <summary>Current account (employee or customer); employees also get effective permissions.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<CurrentUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CurrentUserDto>>> Me(CancellationToken cancellationToken) =>
        Success(await authService.GetCurrentUserAsync(cancellationToken));

    /// <summary>Change the signed-in account's own password.</summary>
    [HttpPut("change-password")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> ChangePassword(
        [FromBody] ChangePasswordDto dto,
        CancellationToken cancellationToken)
    {
        await authService.ChangePasswordAsync(dto, cancellationToken);
        return Success("Password changed");
    }
}
