using System.Linq.Expressions;

namespace CorePlasma.DAL.Interfaces;

/// <summary>
/// Generic repository. Never calls SaveChanges; persistence is committed by <see cref="IUnitOfWork"/>.
/// </summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

    void Update(T entity);

    void Delete(T entity);

    void DeleteRange(IEnumerable<T> entities);

    /// <summary>
    /// Tracked query (use for updates).
    /// </summary>
    IQueryable<T> Query();

    /// <summary>
    /// No-tracking query (use for reads).
    /// </summary>
    IQueryable<T> QueryNoTracking();
}
