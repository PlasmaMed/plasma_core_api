namespace CorePlasma.BAL.DTOs.Permissions;

public sealed class PermissionDto
{
    public Guid Id { get; set; }

    public string PermissionCode { get; set; } = string.Empty;

    public string PermissionName { get; set; } = string.Empty;

    public string ModuleCode { get; set; } = string.Empty;

    public string FunctionCode { get; set; } = string.Empty;

    public string ActionCode { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }
}

public sealed class PermissionFilterDto
{
    public string? ModuleCode { get; set; }

    public string? FunctionCode { get; set; }

    public string? ActionCode { get; set; }

    public string? Keyword { get; set; }

    public bool? IsActive { get; set; }
}
