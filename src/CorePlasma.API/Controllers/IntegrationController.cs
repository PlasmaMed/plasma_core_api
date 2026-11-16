using CorePlasma.API.Authorization;
using CorePlasma.API.Integration;
using CorePlasma.BAL.DTOs.Customers;
using CorePlasma.BAL.DTOs.Engines;
using CorePlasma.BAL.DTOs.Tanks;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Models;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

[Route("api/integration")]
[Authorize(Policy = AuthorizationPolicies.ApiKey)]
public sealed class IntegrationController(
    IEngineService engineService,
    ITankService tankService,
    ICustomerService customerService,
    IntegrationPeerClient peerClient) : ApiControllerBase
{
    /// <summary>Verify service-to-service authentication using the X-API-Key header.</summary>
    [HttpGet("ping")]
    public IActionResult Ping() => Success(new { service = "CorePlasma", connected = true });

    [HttpGet("engines")]
    public async Task<IActionResult> GetEngines([FromQuery] IntegrationReadQuery filter, CancellationToken cancellationToken) =>
        Success(await engineService.GetPagedAsync(new EngineFilterDto
            { Page = filter.Page, PageSize = filter.PageSize, Keyword = filter.Keyword }, cancellationToken));

    [HttpGet("engines/{id:guid}")]
    public async Task<IActionResult> GetEngine(Guid id, CancellationToken cancellationToken) =>
        Success(await engineService.GetByIdAsync(id, cancellationToken));

    [HttpGet("tanks")]
    public async Task<IActionResult> GetTanks([FromQuery] IntegrationReadQuery filter, CancellationToken cancellationToken) =>
        Success(await tankService.GetPagedAsync(new TankFilterDto
            { Page = filter.Page, PageSize = filter.PageSize, Keyword = filter.Keyword }, cancellationToken));

    [HttpGet("tanks/{id:guid}")]
    public async Task<IActionResult> GetTank(Guid id, CancellationToken cancellationToken) =>
        Success(await tankService.GetByIdAsync(id, cancellationToken));

    [HttpGet("customers")]
    public async Task<IActionResult> GetCustomers([FromQuery] IntegrationReadQuery filter, CancellationToken cancellationToken) =>
        Success(await customerService.GetPagedAsync(new CustomerFilterDto
            { Page = filter.Page, PageSize = filter.PageSize, Keyword = filter.Keyword }, cancellationToken));

    [HttpGet("customers/{id:guid}")]
    public async Task<IActionResult> GetCustomer(Guid id, CancellationToken cancellationToken) =>
        Success(await customerService.GetByIdAsync(id, cancellationToken));

    [HttpGet("peer/ping")]
    public Task<IActionResult> PingPeer(CancellationToken cancellationToken) => CallPeer("ping", null, cancellationToken);

    [HttpGet("peer/engines")]
    public Task<IActionResult> GetPeerEngines([FromQuery] IntegrationReadQuery filter, CancellationToken cancellationToken) =>
        CallPeer("engines", filter.ToPagedRequest(), cancellationToken);

    [HttpGet("peer/tanks")]
    public Task<IActionResult> GetPeerTanks([FromQuery] IntegrationReadQuery filter, CancellationToken cancellationToken) =>
        CallPeer("tanks", filter.ToPagedRequest(), cancellationToken);

    [HttpGet("peer/customers")]
    public Task<IActionResult> GetPeerCustomers([FromQuery] IntegrationReadQuery filter, CancellationToken cancellationToken) =>
        CallPeer("customers", filter.ToPagedRequest(), cancellationToken);

    private async Task<IActionResult> CallPeer(string resource, PagedRequest? filter, CancellationToken cancellationToken)
    {
        var result = await peerClient.GetAsync(resource, filter, cancellationToken);
        return result.StatusCode == StatusCodes.Status200OK
            ? Success(result.Data!.Value)
            : StatusCode(result.StatusCode, ApiResponse.Fail(result.Message!));
    }
}
