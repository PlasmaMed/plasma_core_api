using System.Text.Json;
using CorePlasma.BAL.DTOs.Permissions;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Interfaces;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CorePlasma.BAL.Services;

public sealed class PermissionAssignmentService(
    IUnitOfWork unitOfWork,
    IPermissionService permissionService,
    ICurrentUserService currentUser,
    ILogger<PermissionAssignmentService> logger) : IPermissionAssignmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PermissionChangeResultDto> ApplyAsync(
        IReadOnlyCollection<PermissionChange> changes,
        DateTime? expiredDate,
        CancellationToken cancellationToken)
    {
        var result = new PermissionChangeResultDto();
        if (changes.Count == 0)
        {
            return result;
        }

        var distinctChanges = Deduplicate(changes);
        var employeeIds = distinctChanges.Select(x => x.EmployeeId).Distinct().ToList();
        var permissionIds = distinctChanges.Select(x => x.PermissionId).Distinct().ToList();

        // 1. Employees exist.
        var existingEmployeeIds = await unitOfWork.Employees.QueryNoTracking()
            .Where(x => employeeIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var missingEmployees = employeeIds.Except(existingEmployeeIds).ToList();
        if (missingEmployees.Count > 0)
        {
            throw new NotFoundException($"Employee not found: {string.Join(", ", missingEmployees)}");
        }

        // 2. Permissions exist (and are active when granting).
        var permissions = await unitOfWork.Permissions.QueryNoTracking()
            .Where(x => permissionIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PermissionCode, x.IsActive })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var missingPermissions = permissionIds.Where(id => !permissions.ContainsKey(id)).ToList();
        if (missingPermissions.Count > 0)
        {
            throw new NotFoundException($"Permission not found: {string.Join(", ", missingPermissions)}");
        }

        var inactive = distinctChanges
            .Where(x => x.Granted && !permissions[x.PermissionId].IsActive)
            .Select(x => permissions[x.PermissionId].PermissionCode)
            .Distinct()
            .ToList();
        if (inactive.Count > 0)
        {
            throw new BusinessException("Cannot grant inactive permissions.", inactive);
        }

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // 3. Current assignments for the affected pairs (tracked, so we only touch what changes).
            var current = await unitOfWork.EmployeePermissions.Query()
                .Where(x => employeeIds.Contains(x.EmployeeId) && permissionIds.Contains(x.PermissionId))
                .ToListAsync(cancellationToken);
            var currentMap = current.ToDictionary(x => (x.EmployeeId, x.PermissionId));

            var now = DateTime.UtcNow;
            var actorId = currentUser.EmployeeId;
            var auditLogs = new List<PermissionAuditLog>();
            var affectedEmployees = new HashSet<Guid>();

            // 4-6. Diff and apply only the delta.
            foreach (var change in distinctChanges)
            {
                var code = permissions[change.PermissionId].PermissionCode;
                currentMap.TryGetValue((change.EmployeeId, change.PermissionId), out var existing);

                if (change.Granted)
                {
                    if (existing is not null && IsEffective(existing, now))
                    {
                        result.UnchangedCount++;
                        continue;
                    }

                    string? oldValue = null;
                    if (existing is null)
                    {
                        existing = new EmployeePermission
                        {
                            EmployeeId = change.EmployeeId,
                            PermissionId = change.PermissionId
                        };
                        await unitOfWork.EmployeePermissions.AddAsync(existing, cancellationToken);
                    }
                    else
                    {
                        oldValue = Snapshot(existing, code);
                    }

                    existing.IsActive = true;
                    existing.GrantedDate = now;
                    existing.GrantedBy = actorId;
                    existing.ExpiredDate = expiredDate;

                    auditLogs.Add(CreateAuditLog(change, PermissionAuditActions.Grant, oldValue, Snapshot(existing, code), now));
                    result.GrantedCount++;
                }
                else
                {
                    if (existing is null)
                    {
                        result.UnchangedCount++;
                        continue;
                    }

                    var oldValue = Snapshot(existing, code);
                    unitOfWork.EmployeePermissions.Delete(existing);

                    auditLogs.Add(CreateAuditLog(change, PermissionAuditActions.Revoke, oldValue, null, now));
                    result.RevokedCount++;
                }

                affectedEmployees.Add(change.EmployeeId);
            }

            if (auditLogs.Count == 0)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return result;
            }

            // 9. Audit log in the same transaction.
            await unitOfWork.PermissionAuditLogs.AddRangeAsync(auditLogs, cancellationToken);

            // 8. Commit (SaveChanges + commit).
            await unitOfWork.CommitTransactionAsync(cancellationToken);

            // 10. New permissions take effect without re-login.
            await permissionService.InvalidateCacheAsync(affectedEmployees, cancellationToken);

            logger.LogInformation(
                "Permission changes applied by {ActorId}: {Granted} granted, {Revoked} revoked for {EmployeeCount} employees",
                actorId, result.GrantedCount, result.RevokedCount, affectedEmployees.Count);

            return result;
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    private static List<PermissionChange> Deduplicate(IReadOnlyCollection<PermissionChange> changes)
    {
        var conflicts = changes
            .GroupBy(x => (x.EmployeeId, x.PermissionId))
            .Where(g => g.Select(x => x.Granted).Distinct().Count() > 1)
            .Select(g => $"Employee {g.Key.EmployeeId} / Permission {g.Key.PermissionId}")
            .ToList();

        if (conflicts.Count > 0)
        {
            throw new BusinessException("The batch contains conflicting grant/revoke changes.", conflicts);
        }

        return changes.DistinctBy(x => (x.EmployeeId, x.PermissionId)).ToList();
    }

    private static bool IsEffective(EmployeePermission assignment, DateTime now) =>
        assignment.IsActive && (assignment.ExpiredDate is null || assignment.ExpiredDate > now);

    private static string Snapshot(EmployeePermission assignment, string permissionCode) =>
        JsonSerializer.Serialize(new
        {
            permissionCode,
            assignment.IsActive,
            assignment.GrantedDate,
            assignment.GrantedBy,
            assignment.ExpiredDate
        }, JsonOptions);

    private PermissionAuditLog CreateAuditLog(PermissionChange change, string action, string? oldValue, string? newValue, DateTime now) =>
        new()
        {
            EmployeeId = change.EmployeeId,
            PermissionId = change.PermissionId,
            Action = action,
            ChangedBy = currentUser.EmployeeId,
            ChangedDate = now,
            OldValue = oldValue,
            NewValue = newValue,
            IpAddress = currentUser.IpAddress,
            CorrelationId = currentUser.CorrelationId
        };
}
