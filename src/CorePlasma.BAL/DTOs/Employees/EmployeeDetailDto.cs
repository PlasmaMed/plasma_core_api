namespace CorePlasma.BAL.DTOs.Employees;

public class EmployeeDetailDto : EmployeeDto
{
    public DateTime? DateOfBirth { get; set; }

    public string? Address { get; set; }

    public string? IdentityNumber { get; set; }

    public DateTime? JoinDate { get; set; }

    public DateTime? ResignDate { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// Has a login account in EmployeeUsers.
    /// </summary>
    public bool HasLogin { get; set; }

    public Guid? UserId { get; set; }

    public string? Username { get; set; }

    public int PermissionCount { get; set; }

    public int AssignedTankCount { get; set; }

    public int ResponsibleEngineCount { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public Guid? UpdatedBy { get; set; }
}
