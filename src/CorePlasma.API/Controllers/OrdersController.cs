using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Orders;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

/// <summary>Đơn hàng (imported from PLT, read-only).</summary>
[Route("api/orders")]
[HasPermission(PermissionCodes.Order.View)]
public sealed class OrdersController(IOrderService orderService) : ApiControllerBase
{
    /// <summary>Newest first. Filter by status, type, delivery option, store, customer, tank and date range.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OrderDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderDto>>>> GetOrders(
        [FromQuery] OrderFilterDto filter,
        CancellationToken cancellationToken) =>
        Success(await orderService.GetPagedAsync(filter, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<OrderDetailDto>>> GetOrder(Guid id, CancellationToken cancellationToken) =>
        Success(await orderService.GetByIdAsync(id, cancellationToken));
}
