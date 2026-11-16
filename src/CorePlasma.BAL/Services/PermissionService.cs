using AutoMapper;
using CorePlasma.BAL.DTOs.Permissions;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Extensions;
using CorePlasma.Common.Models;
using CorePlasma.Common.Responses;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Extensions;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CorePlasma.BAL.Services;

/// <summary>
/// Cached permission set of one employee (key: permission:{employeeId}).
/// </summary>
public sealed record PermissionCacheEntry(bool IsSuperAdmin, HashSet<string> Codes)
{
    public static PermissionCacheEntry Empty() => new(false, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}

public sealed class PermissionService(
    IUnitOfWork unitOfWork,
    ICacheService cache,
    IMapper mapper,
    IOptions<PermissionCacheSettings> cacheOptions) : IPermissionService
{
    public async Task<bool> HasPermissionAsync(Guid employeeId, string permissionCode, CancellationToken cancellationToken)
    {
        var entry = await GetEntryAsync(employeeId, cancellationToken);
        return entry.IsSuperAdmin || entry.Codes.Contains(permissionCode);
    }

    public async Task<HashSet<string>> GetPermissionsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var entry = await GetEntryAsync(employeeId, cancellationToken);
        return new HashSet<string>(entry.Codes, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> IsSuperAdminAsync(Guid employeeId, CancellationToken cancellationToken) =>
        (await GetEntryAsync(employeeId, cancellationToken)).IsSuperAdmin;

    public async Task<IReadOnlyList<PermissionDto>> GetListAsync(PermissionFilterDto filter, CancellationToken cancellationToken)
    {
        var moduleCode = filter.ModuleCode.TrimToNull();
        var functionCode = filter.FunctionCode.TrimToNull();
        var actionCode = filter.ActionCode.TrimToNull();
        var keyword = filter.Keyword.TrimToNull();

        var query = unitOfWork.Permissions.QueryNoTracking()
            .WhereIf(moduleCode is not null, x => x.ModuleCode == moduleCode)
            .WhereIf(functionCode is not null, x => x.FunctionCode == functionCode)
            .WhereIf(actionCode is not null, x => x.ActionCode == actionCode)
            .WhereContainsAny(unitOfWork.IsSqlServer, keyword, x => x.PermissionCode, x => x.PermissionName)
            .WhereIf(filter.IsActive.HasValue, x => x.IsActive == filter.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.PermissionCode);

        return await mapper.ProjectTo<PermissionDto>(query).ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<PermissionEmployeeDto>> GetEmployeesAsync(
        Guid permissionId,
        PagedRequest request,
        CancellationToken cancellationToken)
    {
        if (!await unitOfWork.Permissions.ExistsAsync(x => x.Id == permissionId, cancellationToken))
        {
            throw new NotFoundException(nameof(Permission), permissionId);
        }

        var now = DateTime.UtcNow;
        var keyword = request.Keyword.TrimToNull();

        var query = unitOfWork.EmployeePermissions.QueryNoTracking()
            .Where(x => x.PermissionId == permissionId && x.IsActive && (x.ExpiredDate == null || x.ExpiredDate > now))
            .WhereContainsAny(unitOfWork.IsSqlServer, keyword, x => x.Employee.EmployeeCode, x => x.Employee.FullName)
            .OrderBy(x => x.Employee.EmployeeCode);

        return await mapper.ProjectTo<PermissionEmployeeDto>(query)
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }

    public async Task<PagedResult<PermissionAuditLogDto>> GetAuditLogsAsync(
        PermissionAuditLogFilterDto filter,
        CancellationToken cancellationToken)
    {
        var action = filter.Action.TrimToNull()?.ToUpperInvariant();

        var logs = unitOfWork.PermissionAuditLogs.QueryNoTracking()
            .WhereIf(filter.EmployeeId.HasValue, x => x.EmployeeId == filter.EmployeeId)
            .WhereIf(filter.PermissionId.HasValue, x => x.PermissionId == filter.PermissionId)
            .WhereIf(filter.ChangedBy.HasValue, x => x.ChangedBy == filter.ChangedBy)
            .WhereIf(action is not null, x => x.Action == action)
            .WhereIf(filter.FromDate.HasValue, x => x.ChangedDate >= filter.FromDate)
            .WhereIf(filter.ToDate.HasValue, x => x.ChangedDate <= filter.ToDate);

        // History must still resolve names of soft-deleted employees/permissions.
        var employees = unitOfWork.Employees.QueryNoTracking().IgnoreQueryFilters();
        var permissions = unitOfWork.Permissions.QueryNoTracking().IgnoreQueryFilters();

        var query =
            from log in logs
            join e in employees on log.EmployeeId equals e.Id into eg
            from e in eg.DefaultIfEmpty()
            join p in permissions on log.PermissionId equals p.Id into pg
            from p in pg.DefaultIfEmpty()
            join c in employees on log.ChangedBy equals (Guid?)c.Id into cg
            from c in cg.DefaultIfEmpty()
            orderby log.ChangedDate descending
            select new PermissionAuditLogDto
            {
                Id = log.Id,
                EmployeeId = log.EmployeeId,
                EmployeeCode = e != null ? e.EmployeeCode : null,
                EmployeeName = e != null ? e.FullName : null,
                PermissionId = log.PermissionId,
                PermissionCode = p != null ? p.PermissionCode : null,
                Action = log.Action,
                ChangedBy = log.ChangedBy,
                ChangedByCode = c != null ? c.EmployeeCode : null,
                ChangedByName = c != null ? c.FullName : null,
                ChangedDate = log.ChangedDate,
                OldValue = log.OldValue,
                NewValue = log.NewValue,
                IpAddress = log.IpAddress,
                CorrelationId = log.CorrelationId
            };

        return await query.ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);
    }

    public async Task InvalidateCacheAsync(IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
    {
        foreach (var employeeId in employeeIds.Distinct())
        {
            await cache.RemoveAsync(CacheKeys.EmployeePermissions(employeeId), cancellationToken);
        }
    }

    private async Task<PermissionCacheEntry> GetEntryAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var key = CacheKeys.EmployeePermissions(employeeId);
        var cached = await cache.GetAsync<PermissionCacheEntry>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (entry, nearestExpiry) = await LoadEntryAsync(employeeId, cancellationToken);

        // Never cache past the moment the next grant expires.
        var ttl = TimeSpan.FromMinutes(Math.Max(1, cacheOptions.Value.ExpirationMinutes));
        if (nearestExpiry.HasValue)
        {
            var untilExpiry = nearestExpiry.Value - DateTime.UtcNow;
            if (untilExpiry < ttl)
            {
                ttl = untilExpiry > TimeSpan.FromSeconds(1) ? untilExpiry : TimeSpan.FromSeconds(1);
            }
        }

        await cache.SetAsync(key, entry, ttl, cancellationToken);
        return entry;
    }

    private async Task<(PermissionCacheEntry Entry, DateTime? NearestExpiry)> LoadEntryAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        // Effective only while both the employee and its login account are active.
        var employee = await unitOfWork.Employees.QueryNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => new
            {
                x.Status,
                AccountActive = x.User != null && x.User.Status == UserStatus.Active,
                IsSuperAdmin = x.User != null && x.User.IsSuperAdmin
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (employee is null || employee.Status != EmployeeStatus.Active || !employee.AccountActive)
        {
            return (PermissionCacheEntry.Empty(), null);
        }

        if (employee.IsSuperAdmin)
        {
            var allCodes = await unitOfWork.Permissions.QueryNoTracking()
                .Where(x => x.IsActive)
                .Select(x => x.PermissionCode)
                .ToListAsync(cancellationToken);

            return (new PermissionCacheEntry(true, allCodes.ToHashSet(StringComparer.OrdinalIgnoreCase)), null);
        }

        var now = DateTime.UtcNow;
        var grants = await unitOfWork.EmployeePermissions.QueryNoTracking()
            .Where(x => x.EmployeeId == employeeId
                        && x.IsActive
                        && x.Permission.IsActive
                        && (x.ExpiredDate == null || x.ExpiredDate > now))
            .Select(x => new { x.Permission.PermissionCode, x.ExpiredDate })
            .ToListAsync(cancellationToken);

        var codes = grants.Select(x => x.PermissionCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nearestExpiry = grants.Where(x => x.ExpiredDate.HasValue).Min(x => x.ExpiredDate);

        return (new PermissionCacheEntry(false, codes), nearestExpiry);
    }
}
