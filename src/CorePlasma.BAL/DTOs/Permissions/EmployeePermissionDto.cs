namespace CorePlasma.BAL.DTOs.Permissions;

public sealed class EmployeePermissionDto
{
    public Guid EmployeeId { get; set; }

    public Guid PermissionId { get; set; }

    public string PermissionCode { get; set; } = string.Empty;

    public string PermissionName { get; set; } = string.Empty;

    public string ModuleCode { get; set; } = string.Empty;

    public string FunctionCode { get; set; } = string.Empty;

    public string ActionCode { get; set; } = string.Empty;

    public DateTime GrantedDate { get; set; }

    public Guid? GrantedBy { get; set; }

    public DateTime? ExpiredDate { get; set; }

    public bool IsActive { get; set; }

    /// <summary>
    /// Active and not expired.
    /// </summary>
    public bool IsEffective { get; set; }
}

/// <summary>
/// Replaces the full permission set of an employee (PUT).
/// </summary>
public sealed class UpdateEmployeePermissionsDto
{
    public List<Guid> PermissionIds { get; set; } = [];
}

/// <summary>
/// Grants additional permissions to an employee (POST).
/// </summary>
public sealed class AddEmployeePermissionsDto
{
    public List<Guid> PermissionIds { get; set; } = [];

    public DateTime? ExpiredDate { get; set; }
}

public sealed class PermissionEmployeeDto
{
    public Guid EmployeeId { get; set; }

    public string EmployeeCode { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Department { get; set; }

    public string? Position { get; set; }

    public DateTime GrantedDate { get; set; }

    public DateTime? ExpiredDate { get; set; }
}

public sealed class PermissionChangeResultDto
{
    public int GrantedCount { get; set; }

    public int RevokedCount { get; set; }

    public int UnchangedCount { get; set; }
}
