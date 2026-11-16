namespace CorePlasma.Common.Models;

public sealed class PermissionCacheSettings
{
    public const string SectionName = "PermissionCache";

    public int ExpirationMinutes { get; set; } = 10;
}
