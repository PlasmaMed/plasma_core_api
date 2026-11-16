namespace CorePlasma.Common.Enums;

public enum CustomerStatus
{
    Active = 1,
    Inactive = 2,
    Suspended = 3,
    /// <summary>
    /// Set automatically when the record is deleted (soft delete); never assigned through create/update.
    /// </summary>
    Deleted = 4
}
