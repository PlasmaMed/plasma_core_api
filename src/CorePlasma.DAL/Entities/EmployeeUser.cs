using CorePlasma.Common.Enums;

namespace CorePlasma.DAL.Entities;

/// <summary>
/// Login account of an internal employee (1 Employee ↔ 0..1 EmployeeUser).
/// Authorization uses the permission matrix of the linked Employee.
/// </summary>
public class EmployeeUser : BaseEntity, IUserAccount, IHasDeletedStatus
{
    public Guid EmployeeId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserStatus Status { get; set; } = UserStatus.Active;

    /// <summary>
    /// Super Admin has every permission. Set by configuration/seed, never by a hard-coded id.
    /// </summary>
    public bool IsSuperAdmin { get; set; }

    public int AccessFailedCount { get; set; }

    public DateTime? LockoutEnd { get; set; }

    public DateTime? LastLoginDate { get; set; }

    public Employee Employee { get; set; } = null!;

    public void MarkAsDeleted() => Status = UserStatus.Deleted;
}
