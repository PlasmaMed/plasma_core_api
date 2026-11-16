using CorePlasma.Common.Enums;
using CorePlasma.Common.Models;

namespace CorePlasma.BAL.DTOs.Users;

public sealed class EmployeeUserDto
{
    public Guid Id { get; set; }

    public long? QuanLyId { get; set; }

    public long? NewAppId { get; set; }

    public Guid EmployeeId { get; set; }

    public string EmployeeCode { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public string? Department { get; set; }

    public string Username { get; set; } = string.Empty;

    public UserStatus Status { get; set; }

    public bool IsSuperAdmin { get; set; }

    public DateTime? LastLoginDate { get; set; }

    public DateTime? LockoutEnd { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }
}

public sealed class CreateEmployeeUserDto
{
    public Guid EmployeeId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public UserStatus Status { get; set; } = UserStatus.Active;
}

public sealed class UpdateEmployeeUserDto
{
    public string Username { get; set; } = string.Empty;

    public UserStatus Status { get; set; } = UserStatus.Active;
}

public sealed class EmployeeUserFilterDto : PagedRequest
{
    public UserStatus? Status { get; set; }

    public string? Department { get; set; }

    /// <summary>Login account(s) of one employee.</summary>
    public Guid? EmployeeId { get; set; }

    public bool? IsSuperAdmin { get; set; }
}
