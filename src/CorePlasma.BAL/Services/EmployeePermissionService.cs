using CorePlasma.BAL.DTOs.Permissions;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Exceptions;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services;

public sealed class EmployeePermissionService(
    IUnitOfWork unitOfWork,
    IPermissionAssignmentService assignmentService) : IEmployeePermissionService
{
    public async Task<IReadOnlyList<EmployeePermissionDto>> GetByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        await EnsureEmployeeExistsAsync(employeeId, cancellationToken);

        var now = DateTime.UtcNow;
        return await unitOfWork.EmployeePermissions.QueryNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderBy(x => x.Permission.SortOrder)
            .ThenBy(x => x.Permission.PermissionCode)
            .Select(x => new EmployeePermissionDto
            {
                EmployeeId = x.EmployeeId,
                PermissionId = x.PermissionId,
                PermissionCode = x.Permission.PermissionCode,
                PermissionName = x.Permission.PermissionName,
                ModuleCode = x.Permission.ModuleCode,
                FunctionCode = x.Permission.FunctionCode,
                ActionCode = x.Permission.ActionCode,
                GrantedDate = x.GrantedDate,
                GrantedBy = x.GrantedBy,
                ExpiredDate = x.ExpiredDate,
                IsActive = x.IsActive,
                IsEffective = x.IsActive && (x.ExpiredDate == null || x.ExpiredDate > now)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<PermissionChangeResultDto> ReplaceAsync(
        Guid employeeId,
        UpdateEmployeePermissionsDto dto,
        CancellationToken cancellationToken)
    {
        await EnsureEmployeeExistsAsync(employeeId, cancellationToken);

        var now = DateTime.UtcNow;
        var requested = dto.PermissionIds.ToHashSet();

        var current = await unitOfWork.EmployeePermissions.QueryNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .Select(x => new
            {
                x.PermissionId,
                IsEffective = x.IsActive && (x.ExpiredDate == null || x.ExpiredDate > now)
            })
            .ToListAsync(cancellationToken);

        var effective = current.Where(x => x.IsEffective).Select(x => x.PermissionId).ToHashSet();

        var toGrant = requested
            .Where(id => !effective.Contains(id))
            .Select(id => new PermissionChange(employeeId, id, true));

        var toRevoke = current
            .Where(x => !requested.Contains(x.PermissionId))
            .Select(x => new PermissionChange(employeeId, x.PermissionId, false));

        var changes = toGrant.Concat(toRevoke).ToList();
        var result = await assignmentService.ApplyAsync(changes, null, cancellationToken);
        result.UnchangedCount += requested.Count(effective.Contains);
        return result;
    }

    public async Task<PermissionChangeResultDto> GrantAsync(
        Guid employeeId,
        AddEmployeePermissionsDto dto,
        CancellationToken cancellationToken)
    {
        await EnsureEmployeeExistsAsync(employeeId, cancellationToken);

        var changes = dto.PermissionIds
            .Distinct()
            .Select(id => new PermissionChange(employeeId, id, true))
            .ToList();

        return await assignmentService.ApplyAsync(changes, dto.ExpiredDate, cancellationToken);
    }

    public async Task RevokeAsync(Guid employeeId, Guid permissionId, CancellationToken cancellationToken)
    {
        await EnsureEmployeeExistsAsync(employeeId, cancellationToken);

        if (!await unitOfWork.EmployeePermissions.ExistsAsync(
                x => x.EmployeeId == employeeId && x.PermissionId == permissionId, cancellationToken))
        {
            throw new NotFoundException("The employee does not have this permission.");
        }

        await assignmentService.ApplyAsync([new PermissionChange(employeeId, permissionId, false)], null, cancellationToken);
    }

    private async Task EnsureEmployeeExistsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (!await unitOfWork.Employees.ExistsAsync(x => x.Id == employeeId, cancellationToken))
        {
            throw new NotFoundException(nameof(Employee), employeeId);
        }
    }
}
