namespace CorePlasma.DAL.Entities;

public class Permission : BaseEntity
{
    public string PermissionCode { get; set; } = string.Empty;

    public string PermissionName { get; set; } = string.Empty;

    public string ModuleCode { get; set; } = string.Empty;

    public string FunctionCode { get; set; } = string.Empty;

    public string ActionCode { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<EmployeePermission> EmployeePermissions { get; set; } = new List<EmployeePermission>();
}
