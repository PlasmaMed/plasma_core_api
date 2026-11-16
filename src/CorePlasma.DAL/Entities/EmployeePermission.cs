namespace CorePlasma.DAL.Entities;

/// <summary>
/// Employee N ↔ N Permission. Composite key (EmployeeId, PermissionId).
/// </summary>
public class EmployeePermission
{
    public Guid EmployeeId { get; set; }

    public Guid PermissionId { get; set; }

    public DateTime GrantedDate { get; set; }

    public Guid? GrantedBy { get; set; }

    public DateTime? ExpiredDate { get; set; }

    public bool IsActive { get; set; } = true;

    public Employee Employee { get; set; } = null!;

    public Permission Permission { get; set; } = null!;
}
