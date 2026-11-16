using CorePlasma.BAL.DTOs.Permissions;

namespace CorePlasma.BAL.Interfaces;

public interface IEmployeePermissionService
{
    Task<IReadOnlyList<EmployeePermissionDto>> GetByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);

    Task<PermissionChangeResultDto> ReplaceAsync(Guid employeeId, UpdateEmployeePermissionsDto dto, CancellationToken cancellationToken);

    Task<PermissionChangeResultDto> GrantAsync(Guid employeeId, AddEmployeePermissionsDto dto, CancellationToken cancellationToken);

    Task RevokeAsync(Guid employeeId, Guid permissionId, CancellationToken cancellationToken);
}
