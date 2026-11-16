using CorePlasma.BAL.DTOs.Users;
using CorePlasma.Common.Responses;

namespace CorePlasma.BAL.Interfaces;

/// <summary>
/// Login accounts of customers (CustomerUsers).
/// </summary>
public interface ICustomerUserService
{
    Task<PagedResult<CustomerUserDto>> GetPagedAsync(CustomerUserFilterDto filter, CancellationToken cancellationToken);

    Task<CustomerUserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<CustomerUserDto> CreateAsync(CreateCustomerUserDto dto, CancellationToken cancellationToken);

    Task<CustomerUserDto> UpdateAsync(Guid id, UpdateCustomerUserDto dto, CancellationToken cancellationToken);

    Task ResetPasswordAsync(Guid id, ResetPasswordDto dto, CancellationToken cancellationToken);

    Task LockAsync(Guid id, CancellationToken cancellationToken);

    Task UnlockAsync(Guid id, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
