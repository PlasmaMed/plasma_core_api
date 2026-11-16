using CorePlasma.BAL.DTOs.Orders;
using CorePlasma.Common.Responses;

namespace CorePlasma.BAL.Interfaces;

/// <summary>Read-only orders imported from the PLT system.</summary>
public interface IOrderService
{
    Task<PagedResult<OrderDto>> GetPagedAsync(OrderFilterDto filter, CancellationToken cancellationToken);

    Task<OrderDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
