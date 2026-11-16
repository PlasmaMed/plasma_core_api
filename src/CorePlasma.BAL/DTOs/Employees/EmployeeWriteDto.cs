using CorePlasma.Common.Enums;

namespace CorePlasma.BAL.DTOs.Employees;

public abstract class EmployeeWriteDto
{
    /// <summary>
    /// Leave empty on create to have the system generate it (DocumentSequences).
    /// </summary>
    public string EmployeeCode { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public DateTime? DateOfBirth { get; set; }

    public Gender Gender { get; set; } = Gender.Unknown;

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? Department { get; set; }

    public string? Position { get; set; }

    public string? IdentityNumber { get; set; }

    public DateTime? JoinDate { get; set; }

    public DateTime? ResignDate { get; set; }

    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    public string? Description { get; set; }
}

public sealed class CreateEmployeeDto : EmployeeWriteDto
{
    /// <summary>
    /// Optional login account created with the employee (same transaction). Both set, or both empty.
    /// Needs EMPLOYEE_USER_CREATE in addition to EMPLOYEE_CREATE.
    /// </summary>
    public string? Username { get; set; }

    public string? Password { get; set; }
}

public sealed class UpdateEmployeeDto : EmployeeWriteDto;
