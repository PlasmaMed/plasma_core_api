using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Interfaces;

namespace CorePlasma.BAL.Services;

public sealed class EngineAccessScope(ICurrentUserService currentUser, IPermissionService permissionService) : IEngineAccessScope
{
    public async Task<Guid?> GetRestrictedEmployeeIdAsync(CancellationToken cancellationToken)
    {
        // Engine endpoints require an employee permission; calls without an employee (startup, imports) are not restricted.
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return null;
        }

        // Super Admin always has every permission.
        return await permissionService.HasPermissionAsync(employeeId, PermissionCodes.Engine.ViewAll, cancellationToken)
            ? null
            : employeeId;
    }
}
