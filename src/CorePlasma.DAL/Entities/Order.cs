using CorePlasma.Common.Enums;

namespace CorePlasma.DAL.Entities;

/// <summary>
/// Đơn hàng giao/thu bình cho khách hàng. Imported from the PLT system (MongoDB plasma_med_center.Invoice);
/// LegacyId is the PLT ObjectId and makes the import repeatable (upsert).
/// </summary>
public class Order : BaseEntity
{
    /// <summary>Source-owned workflow; Core displays the exact external status without executing PLT commands.</summary>
    public bool SourceManaged { get; set; }
    public string? SourceSystem { get; set; }
    public string? SourceRecordId { get; set; }
    public string? SourceStatusCode { get; set; }
    public string? SourceStatusLabel { get; set; }
    public string? SourceDetailsJson { get; set; }
    /// <summary>PLT invoiceIdx (số đơn).</summary>
    public int OrderNumber { get; set; }

    /// <summary>When the order was created in PLT (UTC).</summary>
    public DateTime OrderDate { get; set; }

    public OrderType OrderType { get; set; } = OrderType.Normal;

    public OrderStatus Status { get; set; } = OrderStatus.Request;

    public OrderDeliveryOption? DeliveryOption { get; set; }

    /// <summary>Kho: HN / SG.</summary>
    public string? Store { get; set; }

    public Guid? CustomerId { get; set; }

    /// <summary>Customer login account the order belongs to (PLT clientUserId).</summary>
    public Guid? CustomerUserId { get; set; }

    /// <summary>Employee who created the order (PLT ownerUser, a sales or admin account).</summary>
    public Guid? CreatedByEmployeeId { get; set; }

    /// <summary>Delivery employee (PLT deliveryUserId).</summary>
    public Guid? DeliveryEmployeeId { get; set; }

    public string? Note { get; set; }

    /// <summary>PLT updatedAt (UTC).</summary>
    public DateTime? SourceUpdatedAt { get; set; }

    /// <summary>PLT ObjectId of the invoice.</summary>
    public string? LegacyId { get; set; }

    public Customer? Customer { get; set; }

    public CustomerUser? CustomerUser { get; set; }

    public Employee? CreatedByEmployee { get; set; }

    public Employee? DeliveryEmployee { get; set; }

    public ICollection<OrderTank> Tanks { get; set; } = [];
}
