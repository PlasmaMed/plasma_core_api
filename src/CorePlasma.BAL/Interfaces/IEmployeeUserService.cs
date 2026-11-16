using CorePlasma.BAL.DTOs.Users;
using CorePlasma.Common.Responses;

namespace CorePlasma.BAL.Interfaces;

/// <summary>
/// Login accounts of employees (EmployeeUsers).
/// </summary>
public interface IEmployeeUserService
{
    Task<PagedResult<EmployeeUserDto>> GetPagedAsync(EmployeeUserFilterDto filter, CancellationToken cancellationToken);

    Task<EmployeeUserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<EmployeeUserDto> CreateAsync(CreateEmployeeUserDto dto, CancellationToken cancellationToken);

    Task<EmployeeUserDto> UpdateAsync(Guid id, UpdateEmployeeUserDto dto, CancellationToken cancellationToken);

    Task ResetPasswordAsync(Guid id, ResetPasswordDto dto, CancellationToken cancellationToken);

    Task LockAsync(Guid id, CancellationToken cancellationToken);

    Task UnlockAsync(Guid id, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
