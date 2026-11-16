namespace CorePlasma.Common.Enums;

/// <summary>Order (PLT "Invoice") status, same values as the PLT system.</summary>
public enum OrderStatus
{
    /// <summary>Khách yêu cầu (REQUEST).</summary>
    Request = 1,

    /// <summary>Đã duyệt (APPROVED).</summary>
    Approved = 2,

    /// <summary>Đang giao (DELIVERING).</summary>
    Delivering = 3,

    /// <summary>Đã giao (DELIVERED).</summary>
    Delivered = 4,

    /// <summary>Đã hủy (CANCELLED).</summary>
    Cancelled = 5,
    /// <summary>Exact workflow/status is recorded in SourceStatusCode and SourceStatusLabel.</summary>
    ExternalWorkflow = 100
}

/// <summary>PLT order type.</summary>
public enum OrderType
{
    /// <summary>Đơn thường (NORMAL).</summary>
    Normal = 1,

    /// <summary>Đơn demo / dùng thử (DEMO).</summary>
    Demo = 2,
    /// <summary>Exact source type is retained in SourceDetailsJson (including Quanly test orders).</summary>
    ExternalWorkflow = 100
}

/// <summary>How the tanks are handed over (PLT deliveryOption).</summary>
public enum OrderDeliveryOption
{
    /// <summary>Giao trực tiếp (DIRECT).</summary>
    Direct = 1,

    /// <summary>Khách tự lấy (PICKUP).</summary>
    Pickup = 2,

    /// <summary>Chuyển phát nhanh (EXPRESS).</summary>
    Express = 3
}
