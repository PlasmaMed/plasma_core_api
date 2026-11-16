using CorePlasma.Common.Responses;

namespace CorePlasma.BAL.DTOs.Permissions;

/// <summary>
/// One page of the Employee × Permission checkbox matrix.
/// Columns = filtered permissions, rows = a page of employees.
/// </summary>
public sealed class PermissionMatrixDto
{
    public IReadOnlyList<PermissionMatrixColumnDto> Permissions { get; set; } = [];

    public PagedResult<PermissionMatrixRowDto> Employees { get; set; } = new();
}

public sealed class PermissionMatrixColumnDto
{
    public Guid PermissionId { get; set; }

    public string PermissionCode { get; set; } = string.Empty;

    public string PermissionName { get; set; } = string.Empty;

    public string ModuleCode { get; set; } = string.Empty;

    public string FunctionCode { get; set; } = string.Empty;

    public string ActionCode { get; set; } = string.Empty;
}

public sealed class PermissionMatrixRowDto
{
    public Guid EmployeeId { get; set; }

    public string EmployeeCode { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Department { get; set; }

    public string? Position { get; set; }

    /// <summary>
    /// Super Admin implicitly has every permission (UI should render all checked and locked).
    /// </summary>
    public bool IsSuperAdmin { get; set; }

    /// <summary>
    /// Checked cells: permission ids (among the returned columns) the employee currently holds.
    /// </summary>
    public IReadOnlyList<Guid> GrantedPermissionIds { get; set; } = [];
}

public sealed class PermissionMatrixFilterDto
{
    private int _page = 1;
    private int _pageSize = 50;

    /// <summary>
    /// Matches employee code / full name.
    /// </summary>
    public string? EmployeeKeyword { get; set; }

    public string? Department { get; set; }

    public string? ModuleCode { get; set; }

    public string? FunctionCode { get; set; }

    /// <summary>
    /// Matches permission code / name.
    /// </summary>
    public string? PermissionKeyword { get; set; }

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 50 : Math.Min(value, 200);
    }
}

public sealed class UpdatePermissionMatrixDto
{
    public List<PermissionMatrixChangeDto> Changes { get; set; } = [];
}

public sealed class PermissionMatrixChangeDto
{
    public Guid EmployeeId { get; set; }

    public Guid PermissionId { get; set; }

    public bool Granted { get; set; }
}
