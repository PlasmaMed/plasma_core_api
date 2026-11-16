using System.Linq.Expressions;
using AutoMapper;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Extensions;
using CorePlasma.Common.Models;
using CorePlasma.Common.Responses;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Extensions;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services.Common;

/// <summary>
/// Reusable CRUD for entities derived from <see cref="BaseEntity"/>: paging, keyword/filter, whitelisted sorting,
/// AutoMapper projection, unique checks, optional auto code, soft delete and before/after hooks.
/// A new master-data module only declares <see cref="Repository"/>, <see cref="SortableFields"/> and
/// <see cref="ApplyFilter"/>, and overrides hooks for its business rules.
/// </summary>
public abstract class CrudServiceBase<TEntity, TDto, TDetailDto, TCreateDto, TUpdateDto, TFilter>(
    IUnitOfWork unitOfWork,
    IMapper mapper,
    IDocumentNumberService documentNumberService)
    : ICrudService<TDto, TDetailDto, TCreateDto, TUpdateDto, TFilter>
    where TEntity : BaseEntity
    where TFilter : PagedRequest
{
    protected IUnitOfWork UnitOfWork { get; } = unitOfWork;

    protected IMapper Mapper { get; } = mapper;

    protected IDocumentNumberService DocumentNumbers { get; } = documentNumberService;

    protected abstract IRepository<TEntity> Repository { get; }

    /// <summary>Property names the client may sort by (anything else falls back to the default).</summary>
    protected abstract IReadOnlyCollection<string> SortableFields { get; }

    protected virtual string DefaultSortField => nameof(BaseEntity.CreatedDate);

    protected virtual SortDirection DefaultSortDirection => SortDirection.Desc;

    protected virtual string EntityName => typeof(TEntity).Name;

    /// <summary>Set to fill a code from DocumentSequences when the client leaves it empty.</summary>
    protected virtual AutoCodeOptions<TEntity>? AutoCode => null;

    /// <summary>Run create/update/delete inside a database transaction.</summary>
    protected virtual bool UseTransaction => false;

    /// <summary>
    /// Records the caller may see (row-level access). Applied to list, detail, update and delete;
    /// a record outside the scope is reported as not found.
    /// </summary>
    protected virtual Task<IQueryable<TEntity>> ScopeAsync(IQueryable<TEntity> query, CancellationToken cancellationToken) =>
        Task.FromResult(query);

    /// <summary>Keyword search and module-specific filters.</summary>
    protected abstract IQueryable<TEntity> ApplyFilter(IQueryable<TEntity> query, TFilter filter);

    public virtual async Task<PagedResult<TDto>> GetPagedAsync(TFilter filter, CancellationToken cancellationToken)
    {
        var query = ApplyFilter(await ScopeAsync(Repository.QueryNoTracking(), cancellationToken), filter)
            .ApplySorting(filter.SortBy, filter.SortDirection, SortableFields, DefaultSortField, DefaultSortDirection);

        return await Mapper.ProjectTo<TDto>(query).ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);
    }

    public virtual async Task<TDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await Mapper.ProjectTo<TDetailDto>((await ScopeAsync(Repository.QueryNoTracking(), cancellationToken)).Where(x => x.Id == id))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException(EntityName, id);

    public virtual async Task<TDetailDto> CreateAsync(TCreateDto dto, CancellationToken cancellationToken)
    {
        var entity = Mapper.Map<TEntity>(dto);

        await ExecuteAsync(async () =>
        {
            await BeforeCreateAsync(entity, dto, cancellationToken);
            await AssignAutoCodeAsync(entity, cancellationToken);
            await Repository.AddAsync(entity, cancellationToken);
            await UnitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await AfterCreateAsync(entity, cancellationToken);
        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    public virtual async Task<TDetailDto> UpdateAsync(Guid id, TUpdateDto dto, CancellationToken cancellationToken)
    {
        TEntity entity = null!;

        await ExecuteAsync(async () =>
        {
            entity = await GetEntityAsync(id, cancellationToken);
            await BeforeUpdateAsync(entity, dto, cancellationToken);
            Mapper.Map(dto, entity);
            await UnitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await AfterUpdateAsync(entity, cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public virtual async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        TEntity entity = null!;

        await ExecuteAsync(async () =>
        {
            entity = await GetEntityAsync(id, cancellationToken);
            await BeforeDeleteAsync(entity, cancellationToken);
            Repository.Delete(entity);
            await UnitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await AfterDeleteAsync(entity, cancellationToken);
    }

    /// <summary>Business rules before insert (entity already mapped from the DTO).</summary>
    protected virtual Task BeforeCreateAsync(TEntity entity, TCreateDto dto, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>After commit (cache invalidation, notifications...).</summary>
    protected virtual Task AfterCreateAsync(TEntity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Business rules before the DTO is mapped onto the entity (entity still holds old values).</summary>
    protected virtual Task BeforeUpdateAsync(TEntity entity, TUpdateDto dto, CancellationToken cancellationToken) => Task.CompletedTask;

    protected virtual Task AfterUpdateAsync(TEntity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Throw to block deletion (e.g. record still in use).</summary>
    protected virtual Task BeforeDeleteAsync(TEntity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    protected virtual Task AfterDeleteAsync(TEntity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Tracked entity for update/delete. Override to Include child collections.</summary>
    protected virtual async Task<TEntity> GetEntityAsync(Guid id, CancellationToken cancellationToken) =>
        await Repository.GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundException(EntityName, id);

    /// <summary>
    /// Throws 409 when another record matches <paramref name="predicate"/> (excluding <paramref name="excludeId"/>).
    /// </summary>
    protected async Task EnsureUniqueAsync(
        Expression<Func<TEntity, bool>> predicate,
        Guid? excludeId,
        string message,
        CancellationToken cancellationToken)
    {
        var query = excludeId is { } id ? predicate.AndAlso(x => x.Id != id) : predicate;
        if (await Repository.ExistsAsync(query, cancellationToken))
        {
            throw new ConflictException(message);
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> in a transaction when <see cref="UseTransaction"/> is true
    /// and none is open yet.
    /// </summary>
    protected async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        if (!UseTransaction)
        {
            await action();
            return;
        }

        await UnitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await action();
            await UnitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await UnitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task AssignAutoCodeAsync(TEntity entity, CancellationToken cancellationToken)
    {
        if (AutoCode is not { } options || !string.IsNullOrWhiteSpace(options.GetValue(entity)))
        {
            return;
        }

        var code = await DocumentNumbers.GenerateUniqueAsync(
            options.SequenceCode,
            Repository,
            options.CodeProperty,
            options.DateSelector?.Invoke(entity),
            cancellationToken);

        options.SetValue(entity, code);
    }
}
