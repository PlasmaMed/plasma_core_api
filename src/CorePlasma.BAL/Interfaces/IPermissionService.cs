using CorePlasma.BAL.DTOs.Permissions;
using CorePlasma.Common.Models;
using CorePlasma.Common.Responses;

namespace CorePlasma.BAL.Interfaces;

public interface IPermissionService
{
    /// <summary>
    /// Used by the authorization handler. Cached per employee; Super Admin always returns true.
    /// </summary>
    Task<bool> HasPermissionAsync(Guid employeeId, string permissionCode, CancellationToken cancellationToken);

    /// <summary>
    /// Effective permission codes of an employee (all active codes for Super Admin).
    /// </summary>
    Task<HashSet<string>> GetPermissionsAsync(Guid employeeId, CancellationToken cancellationToken);

    Task<bool> IsSuperAdminAsync(Guid employeeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PermissionDto>> GetListAsync(PermissionFilterDto filter, CancellationToken cancellationToken);

    Task<PagedResult<PermissionEmployeeDto>> GetEmployeesAsync(Guid permissionId, PagedRequest request, CancellationToken cancellationToken);

    Task<PagedResult<PermissionAuditLogDto>> GetAuditLogsAsync(PermissionAuditLogFilterDto filter, CancellationToken cancellationToken);

    Task InvalidateCacheAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken);
}
