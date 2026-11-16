using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Customers;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

[Route("api/customers")]
public sealed class CustomersController(ICustomerService customerService) : ApiControllerBase
{
    /// <summary>Paged list with keyword search, filter and sorting.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.Customer.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CustomerDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<CustomerDto>>>> GetCustomers(
        [FromQuery] CustomerFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await customerService.GetPagedAsync(filter, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.Customer.View)]
    [ProducesResponseType(typeof(ApiResponse<CustomerDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CustomerDetailDto>>> GetCustomer(Guid id, CancellationToken cancellationToken) =>
        Success(await customerService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.Customer.Create)]
    [ProducesResponseType(typeof(ApiResponse<CustomerDetailDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CustomerDetailDto>>> CreateCustomer(
        [FromBody] CreateCustomerDto dto,
        CancellationToken cancellationToken)
    {
        var result = await customerService.CreateAsync(dto, cancellationToken);
        return Created(nameof(GetCustomer), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.Customer.Update)]
    [ProducesResponseType(typeof(ApiResponse<CustomerDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CustomerDetailDto>>> UpdateCustomer(
        Guid id,
        [FromBody] UpdateCustomerDto dto,
        CancellationToken cancellationToken) =>
        Success(await customerService.UpdateAsync(id, dto, cancellationToken));

    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.Customer.Delete)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteCustomer(Guid id, CancellationToken cancellationToken)
    {
        await customerService.DeleteAsync(id, cancellationToken);
        return Success("Deleted");
    }
}
