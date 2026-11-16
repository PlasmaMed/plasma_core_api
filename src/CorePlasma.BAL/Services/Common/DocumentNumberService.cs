using System.Globalization;
using System.Linq.Expressions;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Helpers;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services.Common;

public sealed class DocumentNumberService(IUnitOfWork unitOfWork) : IDocumentNumberService
{
    private const int MaxUniqueAttempts = 20;

    public async Task<string> GenerateAsync(string sequenceCode, DateTime? documentDate = null, CancellationToken cancellationToken = default)
    {
        var sequence = await GetSequenceAsync(sequenceCode, cancellationToken);
        var date = documentDate ?? DateTimeHelper.VietnamNow;

        var value = await unitOfWork.DocumentSequences.IncrementAsync(sequenceCode, GetPeriod(sequence.ResetPolicy, date), cancellationToken)
                    ?? throw new BusinessException($"Document sequence '{sequenceCode}' is not configured or inactive.");

        return Format(sequence, date, value);
    }

    public async Task<string> PreviewAsync(string sequenceCode, DateTime? documentDate = null, CancellationToken cancellationToken = default)
    {
        var sequence = await GetSequenceAsync(sequenceCode, cancellationToken);
        var date = documentDate ?? DateTimeHelper.VietnamNow;
        var period = GetPeriod(sequence.ResetPolicy, date);
        var next = sequence.CurrentPeriod == period ? sequence.CurrentValue + 1 : 1;

        return Format(sequence, date, next);
    }

    public async Task<string> GenerateUniqueAsync<TEntity>(
        string sequenceCode,
        IRepository<TEntity> repository,
        Expression<Func<TEntity, string>> codeProperty,
        DateTime? documentDate = null,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        for (var attempt = 0; attempt < MaxUniqueAttempts; attempt++)
        {
            var code = await GenerateAsync(sequenceCode, documentDate, cancellationToken);
            var exists = Expression.Lambda<Func<TEntity, bool>>(
                Expression.Equal(codeProperty.Body, Expression.Constant(code)),
                codeProperty.Parameters);

            if (!await repository.ExistsAsync(exists, cancellationToken))
            {
                return code;
            }
        }

        throw new ConflictException(
            $"Could not generate a free number for '{sequenceCode}'. Check the sequence configuration (prefix, current value).");
    }

    /// <summary>
    /// Prefix[sep]date[sep]number — empty parts are skipped.
    /// </summary>
    public static string Format(DocumentSequence sequence, DateTime date, long value)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrEmpty(sequence.Prefix))
        {
            parts.Add(sequence.Prefix);
        }

        if (!string.IsNullOrEmpty(sequence.DateFormat))
        {
            parts.Add(date.ToString(sequence.DateFormat, CultureInfo.InvariantCulture));
        }

        parts.Add(value.ToString(CultureInfo.InvariantCulture).PadLeft(sequence.Padding, '0'));
        return string.Join(sequence.Separator, parts);
    }

    public static string GetPeriod(SequenceResetPolicy policy, DateTime date) => policy switch
    {
        SequenceResetPolicy.Yearly => date.ToString("yyyy", CultureInfo.InvariantCulture),
        SequenceResetPolicy.Monthly => date.ToString("yyyyMM", CultureInfo.InvariantCulture),
        SequenceResetPolicy.Daily => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        _ => string.Empty
    };

    private async Task<DocumentSequence> GetSequenceAsync(string sequenceCode, CancellationToken cancellationToken) =>
        await unitOfWork.DocumentSequences.QueryNoTracking()
            .FirstOrDefaultAsync(x => x.SequenceCode == sequenceCode && x.IsActive, cancellationToken)
        ?? throw new BusinessException($"Document sequence '{sequenceCode}' is not configured or inactive.");
}
