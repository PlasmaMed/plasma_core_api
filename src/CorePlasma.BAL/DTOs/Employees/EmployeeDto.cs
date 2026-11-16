using CorePlasma.Common.Enums;

namespace CorePlasma.BAL.DTOs.Employees;

public class EmployeeDto
{
    /// <summary>Read-only source identifiers. Core Id is independent.</summary>
    public long? QuanLyId { get; set; }

    public long? NewAppId { get; set; }

    public Guid Id { get; set; }

    public string EmployeeCode { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public Gender Gender { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Department { get; set; }

    public string? Position { get; set; }

    public EmployeeStatus Status { get; set; }

    public bool IsSuperAdmin { get; set; }

    public DateTime CreatedDate { get; set; }
}
