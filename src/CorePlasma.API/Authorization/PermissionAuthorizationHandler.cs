using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace CorePlasma.API.Authorization;

/// <summary>
/// Only employee accounts carry an EmployeeId, so customer accounts never satisfy a permission (403).
/// Resolves permissions through <see cref="IPermissionService"/> (cached), so changes take effect
/// without the employee signing in again.
/// </summary>
public sealed class PermissionAuthorizationHandler(
    IPermissionService permissionService,
    ICurrentUserService currentUser,
    IHttpContextAccessor httpContextAccessor,
    ILogger<PermissionAuthorizationHandler> logger) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var employeeId = currentUser.EmployeeId;
        if (employeeId is null)
        {
            return;
        }

        var cancellationToken = httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;

        foreach (var code in requirement.PermissionCodes)
        {
            if (!await permissionService.HasPermissionAsync(employeeId.Value, code, cancellationToken))
            {
                logger.LogWarning("Employee {EmployeeId} denied: missing permission {PermissionCode}", employeeId, code);
                return;
            }
        }

        if (await permissionService.IsSuperAdminAsync(employeeId.Value, cancellationToken))
        {
            logger.LogInformation(
                "Super Admin {EmployeeId} authorized for {PermissionCodes} on {Path}",
                employeeId,
                string.Join(",", requirement.PermissionCodes),
                httpContextAccessor.HttpContext?.Request.Path.Value);
        }

        context.Succeed(requirement);
    }
}
