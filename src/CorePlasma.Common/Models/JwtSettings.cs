namespace CorePlasma.Common.Models;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Signing key. Must be supplied via user-secrets / environment variables, never committed.
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    public int ExpirationMinutes { get; set; } = 60;

    /// <summary>Token lifetime when the user ticks "Duy trì đăng nhập".</summary>
    public int RememberMeDays { get; set; } = 30;
}
