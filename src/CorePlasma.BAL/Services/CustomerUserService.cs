using AutoMapper;
using CorePlasma.BAL.DTOs.Users;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Extensions;
using CorePlasma.Common.Responses;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Extensions;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services;

public sealed class CustomerUserService(IUnitOfWork unitOfWork, IMapper mapper) : ICustomerUserService
{
    private static readonly string[] SortableFields =
    [
        nameof(CustomerUser.Username), nameof(CustomerUser.FullName), nameof(CustomerUser.Status),
        nameof(CustomerUser.LastLoginDate), nameof(CustomerUser.CreatedDate), nameof(CustomerUser.UpdatedDate)
    ];

    public async Task<PagedResult<CustomerUserDto>> GetPagedAsync(CustomerUserFilterDto filter, CancellationToken cancellationToken)
    {
        var keyword = filter.Keyword.TrimToNull();

        var query = unitOfWork.CustomerUsers.QueryNoTracking()
            .WhereContainsAny(unitOfWork.IsSqlServer, keyword,
                x => x.Username, x => x.FullName, x => x.Email,
                x => x.Customer.CustomerCode, x => x.Customer.CustomerName)
            .WhereIf(filter.CustomerId.HasValue, x => x.CustomerId == filter.CustomerId)
            .WhereIf(filter.Status.HasValue, x => x.Status == filter.Status)
            .ApplySorting(filter.SortBy, filter.SortDirection, SortableFields, nameof(CustomerUser.Username));

        return await mapper.ProjectTo<CustomerUserDto>(query)
            .ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);
    }

    public async Task<CustomerUserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await mapper.ProjectTo<CustomerUserDto>(unitOfWork.CustomerUsers.QueryNoTracking().Where(x => x.Id == id))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException(nameof(CustomerUser), id);

    public async Task<CustomerUserDto> CreateAsync(CreateCustomerUserDto dto, CancellationToken cancellationToken)
    {
        var entity = mapper.Map<CustomerUser>(dto);
        UserAccountGuard.SetPassword(entity, dto.Password);

        await unitOfWork.CustomerUsers.AddAsync(entity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    public async Task<CustomerUserDto> UpdateAsync(Guid id, UpdateCustomerUserDto dto, CancellationToken cancellationToken)
    {
        var entity = await GetEntityAsync(id, cancellationToken);

        if (!string.Equals(entity.Username, dto.Username, StringComparison.Ordinal) &&
            await unitOfWork.CustomerUsers.ExistsAsync(x => x.Username == dto.Username && x.Id != id, cancellationToken))
        {
            throw new ConflictException("Username already exists.");
        }

        mapper.Map(dto, entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordDto dto, CancellationToken cancellationToken)
    {
        var entity = await GetEntityAsync(id, cancellationToken);
        UserAccountGuard.SetPassword(entity, dto.NewPassword);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task LockAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await GetEntityAsync(id, cancellationToken);
        UserAccountGuard.Lock(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task UnlockAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await GetEntityAsync(id, cancellationToken);
        UserAccountGuard.Unlock(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await GetEntityAsync(id, cancellationToken);
        unitOfWork.CustomerUsers.Delete(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<CustomerUser> GetEntityAsync(Guid id, CancellationToken cancellationToken) =>
        await unitOfWork.CustomerUsers.GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundException(nameof(CustomerUser), id);
}
