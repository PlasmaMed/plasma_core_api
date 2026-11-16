namespace CorePlasma.Common.Models;

/// <summary>
/// Bootstrap Super Admin: an Employee plus its EmployeeUser account, identified by code/username
/// from configuration, never by a hard-coded id.
/// </summary>
public sealed class SuperAdminSettings
{
    public const string SectionName = "SuperAdmin";

    public string EmployeeCode { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = "Super Administrator";

    public string? Email { get; set; }

    public string? Password { get; set; }
}
