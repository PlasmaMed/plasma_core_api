using Microsoft.AspNetCore.Authorization;

namespace CorePlasma.API.Authorization;

/// <summary>
/// The caller must hold every listed permission code.
/// </summary>
public sealed class PermissionRequirement(IReadOnlyCollection<string> permissionCodes) : IAuthorizationRequirement
{
    public IReadOnlyCollection<string> PermissionCodes { get; } = permissionCodes;
}
