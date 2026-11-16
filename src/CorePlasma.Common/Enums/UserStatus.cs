namespace CorePlasma.Common.Enums;

public enum UserStatus
{
    Active = 1,
    Inactive = 2,

    /// <summary>
    /// Locked by an administrator (LOCK/UNLOCK permission).
    /// </summary>
    Locked = 3,
    /// <summary>
    /// Set automatically when the record is deleted (soft delete); never assigned through create/update.
    /// </summary>
    Deleted = 4
}
