using Microsoft.AspNetCore.Authorization;

namespace CorePlasma.API.Authorization;

/// <summary>
/// Declarative permission check, evaluated centrally by <see cref="PermissionAuthorizationHandler"/>.
/// Multiple codes are combined with AND.
/// </summary>
/// <example>[HasPermission(PermissionCodes.Customer.View)]</example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HasPermissionAttribute(params string[] permissionCodes) : AuthorizeAttribute, IAuthorizationRequirementData
{
    public IReadOnlyCollection<string> PermissionCodes { get; } = permissionCodes;

    public IEnumerable<IAuthorizationRequirement> GetRequirements()
    {
        yield return new PermissionRequirement(PermissionCodes);
    }
}
