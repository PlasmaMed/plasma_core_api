namespace CorePlasma.Common.Enums;

public enum TankStatus
{
    Available = 1,
    InUse = 2,
    Empty = 3,
    Maintenance = 4,
    Inspection = 5,
    Damaged = 6,
    Lost = 7,
    /// <summary>
    /// Set automatically when the record is deleted (soft delete); never assigned through create/update.
    /// </summary>
    Deleted = 8,

    // Statuses of the PlasmaMED source system (imported data).
    /// <summary>Đang ở khách hàng (AT_CLIENT).</summary>
    AtClient = 9,
    /// <summary>Đang nạp khí (LOADING).</summary>
    Loading = 10,
    /// <summary>Bị khóa (LOCKED).</summary>
    Locked = 11,
    /// <summary>Ngừng sử dụng (DISABLED).</summary>
    Inactive = 12,

    /// <summary>Đang trong kho: chưa gán cho nhân viên kinh doanh.</summary>
    InStock = 13
}
