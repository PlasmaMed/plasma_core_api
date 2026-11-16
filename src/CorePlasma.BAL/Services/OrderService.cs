using System.Linq.Expressions;
using CorePlasma.BAL.DTOs.Orders;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Extensions;
using CorePlasma.Common.Interfaces;
using CorePlasma.Common.Responses;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Extensions;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services;

public sealed class OrderService(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    IPermissionService permissionService) : IOrderService
{
    private static readonly IReadOnlyCollection<string> SortableFields =
    [
        nameof(Order.OrderNumber), nameof(Order.OrderDate), nameof(Order.Status), nameof(Order.OrderType),
        nameof(Order.DeliveryOption), nameof(Order.Store)
    ];

    private static readonly Expression<Func<Order, OrderDto>> ToDto = x => new OrderDto
    {
        Id = x.Id,
        OrderNumber = x.OrderNumber,
        OrderDate = x.OrderDate,
        OrderType = x.OrderType,
        Status = x.Status,
        DeliveryOption = x.DeliveryOption,
        Store = x.Store,
        CustomerId = x.CustomerId,
        CustomerCode = x.Customer != null ? x.Customer.CustomerCode : null,
        CustomerName = x.Customer != null ? x.Customer.CustomerName : null,
        CustomerUsername = x.CustomerUser != null ? x.CustomerUser.Username : null,
        CreatedByEmployeeId = x.CreatedByEmployeeId,
        CreatedByName = x.CreatedByEmployee != null ? x.CreatedByEmployee.FullName : null,
        DeliveryEmployeeId = x.DeliveryEmployeeId,
        DeliveryEmployeeName = x.DeliveryEmployee != null ? x.DeliveryEmployee.FullName : null,
        TankCount = x.Tanks.Count,
        Note = x.Note,
        SourceUpdatedAt = x.SourceUpdatedAt,
        LegacyId = x.LegacyId,
        SourceManaged = x.SourceManaged,
        SourceSystem = x.SourceSystem,
        SourceRecordId = x.SourceRecordId,
        SourceStatusCode = x.SourceStatusCode,
        SourceStatusLabel = x.SourceStatusLabel
    };

    public async Task<PagedResult<OrderDto>> GetPagedAsync(OrderFilterDto filter, CancellationToken cancellationToken)
    {
        var keyword = filter.Keyword.TrimToNull();
        var number = int.TryParse(keyword?.TrimStart('#'), out var n) ? n : (int?)null;
        var store = filter.Store.TrimToNull();

        var query = (await ScopeAsync(cancellationToken))
            .WhereContainsAnyOr(unitOfWork.IsSqlServer, keyword, x => number.HasValue && x.OrderNumber == number,
                x => x.Customer != null ? x.Customer.CustomerName : null,
                x => x.Customer != null ? x.Customer.CustomerCode : null, x => x.Note)
            .WhereIf(filter.Status.HasValue, x => x.Status == filter.Status)
            .WhereIf(filter.OrderType.HasValue, x => x.OrderType == filter.OrderType)
            .WhereIf(filter.DeliveryOption.HasValue, x => x.DeliveryOption == filter.DeliveryOption)
            .WhereIf(store is not null, x => x.Store == store)
            .WhereIf(filter.CustomerId.HasValue, x => x.CustomerId == filter.CustomerId)
            .WhereIf(filter.TankId.HasValue, x => x.Tanks.Any(t => t.TankId == filter.TankId))
            .WhereIf(filter.FromDate.HasValue, x => x.OrderDate >= filter.FromDate)
            .WhereIf(filter.ToDate.HasValue, x => x.OrderDate <= filter.ToDate)
            .ApplySorting(filter.SortBy, filter.SortDirection, SortableFields, nameof(Order.OrderDate), SortDirection.Desc);

        return await query.Select(ToDto).ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);
    }

    public async Task<OrderDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await (await ScopeAsync(cancellationToken)).Where(x => x.Id == id).Select(ToDto).FirstOrDefaultAsync(cancellationToken)
                    ?? throw new NotFoundException(nameof(Order), id);

        var detail = new OrderDetailDto();
        foreach (var property in typeof(OrderDto).GetProperties())
        {
            property.SetValue(detail, property.GetValue(order));
        }

        detail.SourceDetailsJson = await (await ScopeAsync(cancellationToken))
            .Where(x => x.Id == id).Select(x => x.SourceDetailsJson).FirstAsync(cancellationToken);

        detail.Tanks = await unitOfWork.OrderTanks.QueryNoTracking()
            .Where(x => x.OrderId == id)
            .OrderBy(x => x.SortOrder)
            .Select(x => new OrderTankDto
            {
                Id = x.Id,
                TankId = x.TankId,
                TankCode = x.Tank != null ? x.Tank.TankCode : null,
                TankType = x.Tank != null ? x.Tank.TankType : null,
                TankStatus = x.Tank != null ? x.Tank.Status : null,
                TankLegacyId = x.TankLegacyId,
                SortOrder = x.SortOrder
            })
            .ToListAsync(cancellationToken);
        return detail;
    }

    /// <summary>
    /// Employees without ORDER_VIEW_ALL only see the orders they created or deliver
    /// (same rule as machines: users see their own records, Super Admin everything).
    /// </summary>
    private async Task<IQueryable<Order>> ScopeAsync(CancellationToken cancellationToken)
    {
        var query = unitOfWork.Orders.QueryNoTracking();
        if (currentUser.EmployeeId is not { } employeeId ||
            await permissionService.HasPermissionAsync(employeeId, PermissionCodes.Order.ViewAll, cancellationToken))
        {
            return query;
        }

        return query.Where(x => x.CreatedByEmployeeId == employeeId || x.DeliveryEmployeeId == employeeId);
    }
}
