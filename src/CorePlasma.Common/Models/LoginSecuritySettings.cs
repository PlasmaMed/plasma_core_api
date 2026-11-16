namespace CorePlasma.Common.Models;

public sealed class LoginSecuritySettings
{
    public const string SectionName = "LoginSecurity";

    /// <summary>
    /// Failed attempts before a temporary lockout.
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;
}
