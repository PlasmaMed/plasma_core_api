using CorePlasma.BAL.DTOs.Permissions;

namespace CorePlasma.BAL.Interfaces;

public sealed record PermissionChange(Guid EmployeeId, Guid PermissionId, bool Granted);

/// <summary>
/// Single write path for grant/revoke: validation, diff, transaction, audit log and cache invalidation.
/// </summary>
public interface IPermissionAssignmentService
{
    Task<PermissionChangeResultDto> ApplyAsync(
        IReadOnlyCollection<PermissionChange> changes,
        DateTime? expiredDate,
        CancellationToken cancellationToken);
}
