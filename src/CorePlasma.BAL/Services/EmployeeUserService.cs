using AutoMapper;
using CorePlasma.BAL.DTOs.Users;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Extensions;
using CorePlasma.Common.Interfaces;
using CorePlasma.Common.Responses;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Extensions;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services;

public sealed class EmployeeUserService(
    IUnitOfWork unitOfWork,
    IMapper mapper,
    IPermissionService permissionService,
    ICurrentUserService currentUser) : IEmployeeUserService
{
    private static readonly string[] SortableFields =
    [
        nameof(EmployeeUser.Username), nameof(EmployeeUser.Status), nameof(EmployeeUser.LastLoginDate),
        nameof(EmployeeUser.CreatedDate), nameof(EmployeeUser.UpdatedDate)
    ];

    public async Task<PagedResult<EmployeeUserDto>> GetPagedAsync(EmployeeUserFilterDto filter, CancellationToken cancellationToken)
    {
        var keyword = filter.Keyword.TrimToNull();
        var department = filter.Department.TrimToNull();

        var query = unitOfWork.EmployeeUsers.QueryNoTracking()
            .WhereContainsAny(unitOfWork.IsSqlServer, keyword,
                x => x.Username, x => x.Employee.EmployeeCode, x => x.Employee.FullName)
            .WhereIf(department is not null, x => x.Employee.Department == department)
            .WhereIf(filter.EmployeeId.HasValue, x => x.EmployeeId == filter.EmployeeId)
            .WhereIf(filter.Status.HasValue, x => x.Status == filter.Status)
            .WhereIf(filter.IsSuperAdmin.HasValue, x => x.IsSuperAdmin == filter.IsSuperAdmin)
            .ApplySorting(filter.SortBy, filter.SortDirection, SortableFields, nameof(EmployeeUser.Username));

        return await mapper.ProjectTo<EmployeeUserDto>(query)
            .ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);
    }

    public async Task<EmployeeUserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await mapper.ProjectTo<EmployeeUserDto>(unitOfWork.EmployeeUsers.QueryNoTracking().Where(x => x.Id == id))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException(nameof(EmployeeUser), id);

    public async Task<EmployeeUserDto> CreateAsync(CreateEmployeeUserDto dto, CancellationToken cancellationToken)
    {
        var entity = new EmployeeUser
        {
            EmployeeId = dto.EmployeeId,
            Username = dto.Username,
            Status = dto.Status
        };
        UserAccountGuard.SetPassword(entity, dto.Password);

        await unitOfWork.EmployeeUsers.AddAsync(entity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await permissionService.InvalidateCacheAsync([dto.EmployeeId], cancellationToken);

        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    public async Task<EmployeeUserDto> UpdateAsync(Guid id, UpdateEmployeeUserDto dto, CancellationToken cancellationToken)
    {
        var entity = await GetEntityAsync(id, cancellationToken);

        if (!string.Equals(entity.Username, dto.Username, StringComparison.Ordinal) &&
            await unitOfWork.EmployeeUsers.ExistsAsync(x => x.Username == dto.Username && x.Id != id, cancellationToken))
        {
            throw new ConflictException("Username already exists.");
        }

        if (dto.Status != UserStatus.Active)
        {
            EnsureCanDisable(entity);
        }

        entity.Username = dto.Username;
        entity.Status = dto.Status;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await permissionService.InvalidateCacheAsync([entity.EmployeeId], cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordDto dto, CancellationToken cancellationToken)
    {
        var entity = await GetEntityAsync(id, cancellationToken);
        await EnsureCanResetPasswordAsync(entity, cancellationToken);
        UserAccountGuard.SetPassword(entity, dto.NewPassword);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task LockAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await GetEntityAsync(id, cancellationToken);
        EnsureCanDisable(entity);

        UserAccountGuard.Lock(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await permissionService.InvalidateCacheAsync([entity.EmployeeId], cancellationToken);
    }

    public async Task UnlockAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await GetEntityAsync(id, cancellationToken);

        UserAccountGuard.Unlock(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await permissionService.InvalidateCacheAsync([entity.EmployeeId], cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await GetEntityAsync(id, cancellationToken);
        EnsureCanDisable(entity);

        unitOfWork.EmployeeUsers.Delete(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await permissionService.InvalidateCacheAsync([entity.EmployeeId], cancellationToken);
    }

    private async Task<EmployeeUser> GetEntityAsync(Guid id, CancellationToken cancellationToken) =>
        await unitOfWork.EmployeeUsers.GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundException(nameof(EmployeeUser), id);

    /// <summary>Only a Super Admin may reset a Super Admin password (otherwise any account manager could take it over).</summary>
    private async Task EnsureCanResetPasswordAsync(EmployeeUser entity, CancellationToken cancellationToken)
    {
        if (entity.IsSuperAdmin &&
            (currentUser.EmployeeId is not { } callerId || !await permissionService.IsSuperAdminAsync(callerId, cancellationToken)))
        {
            throw new ForbiddenException("Only a Super Admin can reset the Super Admin password.");
        }
    }

    private void EnsureCanDisable(EmployeeUser entity)
    {
        if (entity.IsSuperAdmin)
        {
            throw new BusinessException("The Super Admin account cannot be locked, deactivated or deleted.");
        }

        if (currentUser.UserType == UserType.Employee && currentUser.UserId == entity.Id)
        {
            throw new BusinessException("You cannot lock, deactivate or delete your own account.");
        }
    }
}
