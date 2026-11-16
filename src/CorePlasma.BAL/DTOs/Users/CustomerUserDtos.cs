using CorePlasma.Common.Enums;
using CorePlasma.Common.Models;

namespace CorePlasma.BAL.DTOs.Users;

public sealed class CustomerUserDto
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public string CustomerCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Position { get; set; }

    public UserStatus Status { get; set; }

    public DateTime? LastLoginDate { get; set; }

    public DateTime? LockoutEnd { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }
}

public abstract class CustomerUserWriteDto
{
    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Position { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Active;
}

public sealed class CreateCustomerUserDto : CustomerUserWriteDto
{
    public Guid CustomerId { get; set; }

    public string Password { get; set; } = string.Empty;
}

public sealed class UpdateCustomerUserDto : CustomerUserWriteDto;

public sealed class CustomerUserFilterDto : PagedRequest
{
    public Guid? CustomerId { get; set; }

    public UserStatus? Status { get; set; }
}
