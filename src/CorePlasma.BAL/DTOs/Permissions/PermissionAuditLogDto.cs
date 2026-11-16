using CorePlasma.Common.Models;

namespace CorePlasma.BAL.DTOs.Permissions;

public sealed class PermissionAuditLogDto
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public string? EmployeeCode { get; set; }

    public string? EmployeeName { get; set; }

    public Guid PermissionId { get; set; }

    public string? PermissionCode { get; set; }

    public string Action { get; set; } = string.Empty;

    public Guid? ChangedBy { get; set; }

    public string? ChangedByCode { get; set; }

    public string? ChangedByName { get; set; }

    public DateTime ChangedDate { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public string? IpAddress { get; set; }

    public string? CorrelationId { get; set; }
}

public sealed class PermissionAuditLogFilterDto : PagedRequest
{
    public Guid? EmployeeId { get; set; }

    public Guid? PermissionId { get; set; }

    public Guid? ChangedBy { get; set; }

    public string? Action { get; set; }

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }
}
