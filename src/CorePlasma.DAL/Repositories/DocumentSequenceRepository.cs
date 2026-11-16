using CorePlasma.DAL.Context;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.DAL.Repositories;

public sealed class DocumentSequenceRepository(ApplicationDbContext context)
    : Repository<DocumentSequence>(context), IDocumentSequenceRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task<long?> IncrementAsync(string sequenceCode, string period, CancellationToken cancellationToken = default)
    {
        // A single UPDATE locks the row and returns each caller's distinct value.
        var values = _context.Database.IsSqlServer()
            ? await _context.Database.SqlQuery<long>($"""
                UPDATE [DocumentSequences]
                SET [CurrentValue] = CASE WHEN [CurrentPeriod] = {period} THEN [CurrentValue] + 1 ELSE 1 END,
                    [CurrentPeriod] = {period},
                    [UpdatedDate] = SYSUTCDATETIME()
                OUTPUT INSERTED.[CurrentValue] AS [Value]
                WHERE [SequenceCode] = {sequenceCode} AND [IsDeleted] = 0 AND [IsActive] = 1
                """).ToListAsync(cancellationToken)
            : await _context.Database.SqlQuery<long>($"""
                UPDATE "DocumentSequences"
                SET "CurrentValue" = CASE WHEN "CurrentPeriod" = {period} THEN "CurrentValue" + 1 ELSE 1 END,
                    "CurrentPeriod" = {period},
                    "UpdatedDate" = (now() at time zone 'utc')
                WHERE "SequenceCode" = {sequenceCode} AND "IsDeleted" = false AND "IsActive" = true
                RETURNING "CurrentValue" AS "Value"
                """).ToListAsync(cancellationToken);

        return values.Count == 0 ? null : values[0];
    }
}
