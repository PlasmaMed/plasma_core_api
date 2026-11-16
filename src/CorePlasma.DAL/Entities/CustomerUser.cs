using CorePlasma.Common.Enums;

namespace CorePlasma.DAL.Entities;

/// <summary>
/// Login account of a customer contact (1 Customer → N CustomerUser).
/// Only reaches customer-portal endpoints, scoped to its own Customer.
/// </summary>
public class CustomerUser : BaseEntity, IUserAccount, IHasDeletedStatus
{
    public Guid CustomerId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Position { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Active;

    public int AccessFailedCount { get; set; }

    public DateTime? LockoutEnd { get; set; }

    public DateTime? LastLoginDate { get; set; }

    public Customer Customer { get; set; } = null!;

    public void MarkAsDeleted() => Status = UserStatus.Deleted;
}
