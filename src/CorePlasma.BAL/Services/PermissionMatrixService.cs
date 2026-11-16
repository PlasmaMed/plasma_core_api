using AutoMapper;
using CorePlasma.BAL.DTOs.Permissions;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Extensions;
using CorePlasma.DAL.Extensions;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services;

public sealed class PermissionMatrixService(
    IUnitOfWork unitOfWork,
    IMapper mapper,
    IPermissionAssignmentService assignmentService) : IPermissionMatrixService
{
    /// <summary>
    /// Upper bound on columns per request; narrow with Module/Function/Permission filters.
    /// </summary>
    public const int MaxPermissionColumns = 500;

    public async Task<PermissionMatrixDto> GetAsync(PermissionMatrixFilterDto filter, CancellationToken cancellationToken)
    {
        var moduleCode = filter.ModuleCode.TrimToNull();
        var functionCode = filter.FunctionCode.TrimToNull();
        var permissionKeyword = filter.PermissionKeyword.TrimToNull();
        var employeeKeyword = filter.EmployeeKeyword.TrimToNull();
        var department = filter.Department.TrimToNull();

        // Columns: filtered permissions.
        var permissionQuery = unitOfWork.Permissions.QueryNoTracking()
            .Where(x => x.IsActive)
            .WhereIf(moduleCode is not null, x => x.ModuleCode == moduleCode)
            .WhereIf(functionCode is not null, x => x.FunctionCode == functionCode)
            .WhereContainsAny(unitOfWork.IsSqlServer, permissionKeyword, x => x.PermissionCode, x => x.PermissionName)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.PermissionCode)
            .Take(MaxPermissionColumns);

        var columns = await mapper.ProjectTo<PermissionMatrixColumnDto>(permissionQuery).ToListAsync(cancellationToken);

        // Rows: one page of employees.
        var employeeQuery = unitOfWork.Employees.QueryNoTracking()
            .WhereContainsAny(unitOfWork.IsSqlServer, employeeKeyword, x => x.EmployeeCode, x => x.FullName)
            .WhereIf(department is not null, x => x.Department == department)
            .OrderBy(x => x.EmployeeCode)
            .Select(x => new PermissionMatrixRowDto
            {
                EmployeeId = x.Id,
                EmployeeCode = x.EmployeeCode,
                FullName = x.FullName,
                Department = x.Department,
                Position = x.Position,
                IsSuperAdmin = x.User != null && x.User.IsSuperAdmin
            });

        var rows = await employeeQuery.ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);

        // Cells: only grants for (page employees × visible columns).
        var employeeIds = rows.Items.Select(x => x.EmployeeId).ToList();
        var permissionIds = columns.Select(x => x.PermissionId).ToList();

        if (employeeIds.Count > 0 && permissionIds.Count > 0)
        {
            var now = DateTime.UtcNow;
            var grants = await unitOfWork.EmployeePermissions.QueryNoTracking()
                .Where(x => employeeIds.Contains(x.EmployeeId)
                            && permissionIds.Contains(x.PermissionId)
                            && x.IsActive
                            && (x.ExpiredDate == null || x.ExpiredDate > now))
                .Select(x => new { x.EmployeeId, x.PermissionId })
                .ToListAsync(cancellationToken);

            var lookup = grants.ToLookup(x => x.EmployeeId, x => x.PermissionId);
            foreach (var row in rows.Items)
            {
                row.GrantedPermissionIds = row.IsSuperAdmin ? permissionIds : lookup[row.EmployeeId].ToList();
            }
        }

        return new PermissionMatrixDto
        {
            Permissions = columns,
            Employees = rows
        };
    }

    public Task<PermissionChangeResultDto> UpdateAsync(UpdatePermissionMatrixDto dto, CancellationToken cancellationToken)
    {
        var changes = dto.Changes
            .Select(x => new PermissionChange(x.EmployeeId, x.PermissionId, x.Granted))
            .ToList();

        return assignmentService.ApplyAsync(changes, null, cancellationToken);
    }
}
