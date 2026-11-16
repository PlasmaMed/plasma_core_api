using System.Linq.Expressions;
using CorePlasma.DAL.Interfaces;

namespace CorePlasma.BAL.Interfaces;

/// <summary>
/// Generates codes and voucher numbers from DocumentSequences (e.g. KH00001, PDC-202610-0001).
/// </summary>
public interface IDocumentNumberService
{
    /// <summary>
    /// Consumes and returns the next number.
    /// </summary>
    /// <param name="documentDate">Business date (Vietnam time) for the date part / reset period; defaults to today.</param>
    Task<string> GenerateAsync(string sequenceCode, DateTime? documentDate = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Next number without consuming it (for display only; not reserved).
    /// </summary>
    Task<string> PreviewAsync(string sequenceCode, DateTime? documentDate = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="GenerateAsync"/> but skips numbers already used in <paramref name="codeProperty"/>
    /// (e.g. a code typed in manually earlier).
    /// </summary>
    Task<string> GenerateUniqueAsync<TEntity>(
        string sequenceCode,
        IRepository<TEntity> repository,
        Expression<Func<TEntity, string>> codeProperty,
        DateTime? documentDate = null,
        CancellationToken cancellationToken = default) where TEntity : class;
}
