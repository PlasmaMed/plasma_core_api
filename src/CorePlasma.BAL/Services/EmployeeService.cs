using AutoMapper;
using CorePlasma.BAL.DTOs.Employees;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Extensions;
using CorePlasma.Common.Interfaces;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services;

public sealed class EmployeeService(
    IUnitOfWork unitOfWork,
    IMapper mapper,
    IDocumentNumberService documentNumberService,
    IPermissionService permissionService,
    ICurrentUserService currentUser)
    : CrudServiceBase<Employee, EmployeeDto, EmployeeDetailDto, CreateEmployeeDto, UpdateEmployeeDto, EmployeeFilterDto>(
        unitOfWork, mapper, documentNumberService),
      IEmployeeService
{
    protected override IRepository<Employee> Repository => UnitOfWork.Employees;

    protected override IReadOnlyCollection<string> SortableFields =>
    [
        nameof(Employee.EmployeeCode), nameof(Employee.FullName), nameof(Employee.Department),
        nameof(Employee.Position), nameof(Employee.Status), nameof(Employee.JoinDate),
        nameof(Employee.CreatedDate), nameof(Employee.UpdatedDate)
    ];

    protected override string DefaultSortField => nameof(Employee.EmployeeCode);

    protected override SortDirection DefaultSortDirection => SortDirection.Asc;

    protected override AutoCodeOptions<Employee> AutoCode => new(SequenceCodes.Employee, x => x.EmployeeCode);

    protected override IQueryable<Employee> ApplyFilter(IQueryable<Employee> query, EmployeeFilterDto filter)
    {
        var keyword = filter.Keyword.TrimToNull();
        var department = filter.Department.TrimToNull();
        var position = filter.Position.TrimToNull();

        return query
            .WhereContainsAny(UnitOfWork.IsSqlServer, keyword,
                x => x.EmployeeCode, x => x.FullName, x => x.Phone, x => x.Email, x => x.IdentityNumber)
            .WhereIf(department is not null, x => x.Department == department)
            .WhereIf(position is not null, x => x.Position == position)
            .WhereIf(filter.Status.HasValue, x => x.Status == filter.Status)
            .WhereIf(filter.Gender.HasValue, x => x.Gender == filter.Gender);
    }

    /// <summary>Optional login account, saved in the same SaveChanges as the employee (both or neither).</summary>
    protected override async Task BeforeCreateAsync(Employee entity, CreateEmployeeDto dto, CancellationToken cancellationToken)
    {
        var username = dto.Username.TrimToNull();
        if (username is null)
        {
            return;
        }

        if (currentUser.EmployeeId is not { } callerId ||
            !await permissionService.HasPermissionAsync(callerId, PermissionCodes.EmployeeUser.Create, cancellationToken))
        {
            throw new ForbiddenException("Creating a login account needs the EMPLOYEE_USER_CREATE permission.");
        }

        var account = new EmployeeUser { EmployeeId = entity.Id, Username = username, Status = UserStatus.Active };
        UserAccountGuard.SetPassword(account, dto.Password!);
        await UnitOfWork.EmployeeUsers.AddAsync(account, cancellationToken);
    }

    protected override async Task BeforeUpdateAsync(Employee entity, UpdateEmployeeDto dto, CancellationToken cancellationToken)
    {
        await EnsureUniqueAsync(x => x.EmployeeCode == dto.EmployeeCode, entity.Id, "EmployeeCode already exists.", cancellationToken);

        if (dto.Status != EmployeeStatus.Active && await IsSuperAdminAsync(entity.Id, cancellationToken))
        {
            throw new BusinessException("Super Admin must remain active.");
        }
    }

    // Status may have changed: inactive/resigned employees lose effective permissions immediately.
    protected override Task AfterUpdateAsync(Employee entity, CancellationToken cancellationToken) =>
        permissionService.InvalidateCacheAsync([entity.Id], cancellationToken);

    protected override async Task BeforeDeleteAsync(Employee entity, CancellationToken cancellationToken)
    {
        if (await IsSuperAdminAsync(entity.Id, cancellationToken))
        {
            throw new BusinessException("Super Admin cannot be deleted.");
        }

        if (currentUser.EmployeeId == entity.Id)
        {
            throw new BusinessException("You cannot delete your own account.");
        }

        if (await UnitOfWork.Tanks.ExistsAsync(t => t.AssignedEmployeeId == entity.Id, cancellationToken))
        {
            throw new BusinessException("Cannot delete an employee who still has tanks assigned.");
        }

        if (await UnitOfWork.Engines.ExistsAsync(e => e.ResponsibleEmployeeId == entity.Id, cancellationToken))
        {
            throw new BusinessException("Cannot delete an employee who is still responsible for engines.");
        }

        // The login account goes with the employee (both soft-deleted in one SaveChanges).
        var account = await UnitOfWork.EmployeeUsers.Query()
            .FirstOrDefaultAsync(x => x.EmployeeId == entity.Id, cancellationToken);
        if (account is not null)
        {
            UnitOfWork.EmployeeUsers.Delete(account);
        }
    }

    protected override Task AfterDeleteAsync(Employee entity, CancellationToken cancellationToken) =>
        permissionService.InvalidateCacheAsync([entity.Id], cancellationToken);

    private Task<bool> IsSuperAdminAsync(Guid employeeId, CancellationToken cancellationToken) =>
        UnitOfWork.EmployeeUsers.ExistsAsync(x => x.EmployeeId == employeeId && x.IsSuperAdmin, cancellationToken);
}
