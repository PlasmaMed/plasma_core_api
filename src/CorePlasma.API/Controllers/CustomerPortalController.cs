using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Tanks;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

/// <summary>
/// Endpoints for customer accounts (CustomerUsers). Data is always limited to the caller's own customer.
/// </summary>
[Route("api/customer-portal")]
[Authorize(Policy = AuthorizationPolicies.CustomerUser)]
public sealed class CustomerPortalController(ICustomerPortalService customerPortalService) : ApiControllerBase
{
    /// <summary>Tanks belonging to the signed-in customer.</summary>
    [HttpGet("tanks")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TankDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<TankDto>>>> GetMyTanks(
        [FromQuery] TankFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await customerPortalService.GetMyTanksAsync(filter, cancellationToken));

    [HttpGet("tanks/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<TankDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TankDetailDto>>> GetMyTank(Guid id, CancellationToken cancellationToken) =>
        Success(await customerPortalService.GetMyTankAsync(id, cancellationToken));
}
