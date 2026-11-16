namespace CorePlasma.Common.Enums;

public enum EmployeeStatus
{
    Active = 1,
    Inactive = 2,
    OnLeave = 3,
    Resigned = 4,
    /// <summary>
    /// Set automatically when the record is deleted (soft delete); never assigned through create/update.
    /// </summary>
    Deleted = 5
}
