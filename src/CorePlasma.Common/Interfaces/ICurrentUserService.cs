using CorePlasma.Common.Enums;

namespace CorePlasma.Common.Interfaces;

/// <summary>
/// Information about the current request's caller. Implemented by the API layer.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// Login account id (EmployeeUsers.Id or CustomerUsers.Id).
    /// </summary>
    Guid? UserId { get; }

    UserType? UserType { get; }

    string? Username { get; }

    /// <summary>
    /// Set only when the caller is an employee account.
    /// </summary>
    Guid? EmployeeId { get; }

    /// <summary>
    /// Set only when the caller is a customer account.
    /// </summary>
    Guid? CustomerId { get; }

    /// <summary>
    /// Id written to audit columns (CreatedBy/UpdatedBy): EmployeeId for employee accounts,
    /// CustomerUsers.Id for customer accounts.
    /// </summary>
    Guid? ActorId { get; }

    bool IsAuthenticated { get; }

    string? IpAddress { get; }

    string? CorrelationId { get; }
}
