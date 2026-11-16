namespace CorePlasma.DAL.Entities;

/// <summary>
/// Immutable history of permission grants/revokes. Intentionally has no FK so history survives
/// changes to employees and permissions.
/// </summary>
public class PermissionAuditLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid EmployeeId { get; set; }

    public Guid PermissionId { get; set; }

    public string Action { get; set; } = string.Empty;

    public Guid? ChangedBy { get; set; }

    public DateTime ChangedDate { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public string? IpAddress { get; set; }

    public string? CorrelationId { get; set; }
}
