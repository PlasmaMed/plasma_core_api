using CorePlasma.Common.Enums;

namespace CorePlasma.DAL.Entities;

/// <summary>
/// Credential/lockout fields shared by <see cref="EmployeeUser"/> and <see cref="CustomerUser"/>,
/// so sign-in logic is written once while the two account types stay in separate tables.
/// </summary>
public interface IUserAccount
{
    Guid Id { get; }

    string Username { get; set; }

    string PasswordHash { get; set; }

    UserStatus Status { get; set; }

    int AccessFailedCount { get; set; }

    DateTime? LockoutEnd { get; set; }

    DateTime? LastLoginDate { get; set; }
}
