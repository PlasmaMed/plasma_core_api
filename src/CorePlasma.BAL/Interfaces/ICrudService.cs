using CorePlasma.Common.Responses;

namespace CorePlasma.BAL.Interfaces;

/// <summary>
/// Standard CRUD contract shared by master-data services (Customer, Employee, Tank, Engine, ...).
/// Implement by inheriting <see cref="Services.Common.CrudServiceBase{TEntity,TDto,TDetailDto,TCreateDto,TUpdateDto,TFilter}"/>.
/// </summary>
public interface ICrudService<TDto, TDetailDto, in TCreateDto, in TUpdateDto, in TFilter>
{
    Task<PagedResult<TDto>> GetPagedAsync(TFilter filter, CancellationToken cancellationToken);

    Task<TDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<TDetailDto> CreateAsync(TCreateDto dto, CancellationToken cancellationToken);

    Task<TDetailDto> UpdateAsync(Guid id, TUpdateDto dto, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
