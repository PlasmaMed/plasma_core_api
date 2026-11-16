using CorePlasma.Common.Enums;

namespace CorePlasma.DAL.Entities;

public class Employee : BaseEntity, IHasDeletedStatus, ISourceIdentified
{
    /// <summary>Representative numeric source ID; all verified aliases remain in LegacySyncMappings.</summary>
    public long? QuanLyId { get; set; }

    public long? NewAppId { get; set; }

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

    /// <summary>
    /// Login account (table EmployeeUsers). Null means the employee cannot sign in.
    /// </summary>
    public EmployeeUser? User { get; set; }

    public ICollection<EmployeePermission> EmployeePermissions { get; set; } = new List<EmployeePermission>();

    public ICollection<Tank> AssignedTanks { get; set; } = new List<Tank>();

    public ICollection<Engine> ResponsibleEngines { get; set; } = new List<Engine>();

    public void MarkAsDeleted() => Status = EmployeeStatus.Deleted;
}
