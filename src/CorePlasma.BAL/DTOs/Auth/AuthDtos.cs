using CorePlasma.Common.Enums;

namespace CorePlasma.BAL.DTOs.Auth;

public sealed class LoginRequestDto
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>"Duy trì đăng nhập": long-lived token (Jwt:RememberMeDays) instead of Jwt:ExpirationMinutes.</summary>
    public bool RememberMe { get; set; }
}

public sealed class LoginResponseDto
{
    public string AccessToken { get; set; } = string.Empty;

    public string TokenType { get; set; } = "Bearer";

    public DateTime ExpiresAt { get; set; }

    public CurrentUserDto User { get; set; } = new();
}

public sealed class ChangePasswordDto
{
    public string CurrentPassword { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;
}

public sealed class CurrentUserDto
{
    public Guid UserId { get; set; }

    public UserType UserType { get; set; }

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Employee accounts only.
    /// </summary>
    public Guid? EmployeeId { get; set; }

    public string? EmployeeCode { get; set; }

    /// <summary>
    /// Customer accounts only.
    /// </summary>
    public Guid? CustomerId { get; set; }

    public string? CustomerCode { get; set; }

    public string? CustomerName { get; set; }

    public bool IsSuperAdmin { get; set; }

    /// <summary>
    /// Effective permission codes (employee accounts). Empty for customer accounts.
    /// </summary>
    public IReadOnlyCollection<string> Permissions { get; set; } = [];
}

/// <summary>
/// Identity data needed to issue a JWT.
/// </summary>
public sealed record TokenSubject(
    Guid UserId,
    UserType UserType,
    string Username,
    string FullName,
    Guid? EmployeeId,
    Guid? CustomerId);

public sealed record AccessToken(string Token, DateTime ExpiresAt);
