namespace CorePlasma.BAL.DTOs.Users;

/// <summary>
/// Administrator sets a new password for an account (employee or customer).
/// </summary>
public sealed class ResetPasswordDto
{
    public string NewPassword { get; set; } = string.Empty;
}
