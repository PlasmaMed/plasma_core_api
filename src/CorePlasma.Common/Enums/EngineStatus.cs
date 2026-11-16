namespace CorePlasma.Common.Enums;

public enum EngineStatus
{
    Active = 1,
    Inactive = 2,
    Maintenance = 3,
    Repair = 4,
    Broken = 5,
    Disposed = 6,
    /// <summary>
    /// Set automatically when the record is deleted (soft delete); never assigned through create/update.
    /// </summary>
    Deleted = 7,

    // Statuses of the PlasmaMED source system (imported data).
    /// <summary>Sẵn sàng sử dụng (READY_TO_USE).</summary>
    ReadyToUse = 8,
    /// <summary>Cho mượn (LENT).</summary>
    Lent = 9,
    /// <summary>Cho thuê (RENTED).</summary>
    Rented = 10,
    /// <summary>Đã bán (SOLD).</summary>
    Sold = 11,
    /// <summary>Đang kiểm tra (CHECKING).</summary>
    Checking = 12,

    /// <summary>Đang trong kho: chưa gán cho nhân viên kinh doanh.</summary>
    InStock = 13
}
