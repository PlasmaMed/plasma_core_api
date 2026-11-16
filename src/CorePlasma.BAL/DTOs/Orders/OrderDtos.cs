using CorePlasma.Common.Enums;
using CorePlasma.Common.Models;

namespace CorePlasma.BAL.DTOs.Orders;

/// <summary>Đơn hàng (list row).</summary>
public class OrderDto
{
    public Guid Id { get; set; }

    public int OrderNumber { get; set; }

    /// <summary>UTC.</summary>
    public DateTime OrderDate { get; set; }

    public OrderType OrderType { get; set; }

    public OrderStatus Status { get; set; }

    public OrderDeliveryOption? DeliveryOption { get; set; }

    public string? Store { get; set; }

    public Guid? CustomerId { get; set; }

    public string? CustomerCode { get; set; }

    public string? CustomerName { get; set; }

    public string? CustomerUsername { get; set; }

    public Guid? CreatedByEmployeeId { get; set; }

    public string? CreatedByName { get; set; }

    public Guid? DeliveryEmployeeId { get; set; }

    public string? DeliveryEmployeeName { get; set; }

    public int TankCount { get; set; }

    public string? Note { get; set; }

    public DateTime? SourceUpdatedAt { get; set; }

    public string? LegacyId { get; set; }

    public bool SourceManaged { get; set; }

    public string? SourceSystem { get; set; }

    public string? SourceRecordId { get; set; }

    public string? SourceStatusCode { get; set; }

    public string? SourceStatusLabel { get; set; }
}

public sealed class OrderDetailDto : OrderDto
{
    public List<OrderTankDto> Tanks { get; set; } = [];

    public string? SourceDetailsJson { get; set; }
}

public sealed class OrderTankDto
{
    public Guid Id { get; set; }

    public Guid? TankId { get; set; }

    /// <summary>Null when the PLT tank is unknown in this system (TankLegacyId is then shown).</summary>
    public string? TankCode { get; set; }

    public string? TankType { get; set; }

    public TankStatus? TankStatus { get; set; }

    public string? TankLegacyId { get; set; }

    public int SortOrder { get; set; }
}

public sealed class OrderFilterDto : PagedRequest
{
    public OrderStatus? Status { get; set; }

    public OrderType? OrderType { get; set; }

    public OrderDeliveryOption? DeliveryOption { get; set; }

    public string? Store { get; set; }

    public Guid? CustomerId { get; set; }

    /// <summary>Orders containing this tank.</summary>
    public Guid? TankId { get; set; }

    /// <summary>Orders created at or after this time (UTC).</summary>
    public DateTime? FromDate { get; set; }

    /// <summary>Orders created at or before this time (UTC).</summary>
    public DateTime? ToDate { get; set; }
}
