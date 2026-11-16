using System.Data;
using System.Security.Cryptography;
using CorePlasma.Common.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace CorePlasma.API.SourceSync;

public sealed partial class SourceSyncStore
{
    private sealed class Digest
    {
        public long Count { get; private set; }
        public byte[] Sum { get; } = new byte[32];
        public byte[] Xor { get; } = new byte[32];
        public void Add(byte[] hash) => Merge(hash, hash, 1);
        public void Merge(byte[] sum, byte[] xor, long count)
        {
            if (sum.Length != 32 || xor.Length != 32 || count < 0) throw Conflict("Invalid source digest.");
            var carry = 0;
            for (var index = 31; index >= 0; index--)
            {
                carry += Sum[index] + sum[index];
                Sum[index] = (byte)carry;
                carry >>= 8;
                Xor[index] ^= xor[index];
            }
            Count = checked(Count + count);
        }
        public bool Matches(Digest other) => Count == other.Count
            && CryptographicOperations.FixedTimeEquals(Sum, other.Sum) && CryptographicOperations.FixedTimeEquals(Xor, other.Xor);
    }

    /// <summary>One durable table phase. Long scans/index builds run outside HTTP request lifetimes.</summary>
    public async Task<bool> FinalizeNextAsync(string source, CancellationToken ct)
    {
        await using var db = await OpenAsync(source, ct);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(ct);
        await LockAsync(db, tx, source, ct);
        var bound = await BindingAsync(db, tx, source, ct);
        if (bound is null || bound.State != "finalizing" || bound.ErrorCode is not null) return false;
        var tables = await TablesAsync(db, tx, source, ct);
        var table = tables.FirstOrDefault(item => item.State != "ready");
        if (table is null)
        {
            await ExecuteAsync(db, tx, "UPDATE source_sync.sources SET state='ready',updated_at=SYSUTCDATETIME() WHERE source_id=@source", ct, ("source", source));
            await tx.CommitAsync(ct);
            return true;
        }
        await OwnedAsync(db, tx, source, table, ct);
        if (table.State == "seeding")
        {
            var expected = new Digest();
            await using (var chunks = Command(db, tx, "SELECT row_count,digest_sum,digest_xor FROM source_sync.chunks WHERE source_id=@source AND generation=@generation AND table_name=@table",
                ("source", source), ("generation", bound.Generation), ("table", table.Table.Name)))
            await using (var reader = await chunks.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) expected.Merge(reader.GetFieldValue<byte[]>(1), reader.GetFieldValue<byte[]>(2), reader.GetInt64(0));
            var actual = new Digest();
            await using (var scan = Command(db, tx, "SELECT " + Columns(table.Table) + ",[__ss_key_hash],[__ss_row_hash] FROM " + Name(source, table.Table.Name)))
            {
                scan.CommandTimeout = 0;
                await using var reader = await scan.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
                while (await reader.ReadAsync(ct))
                {
                    var values = new string?[table.Table.Columns.Length];
                    for (var index = 0; index < values.Length; index++) values[index] = reader.IsDBNull(index) ? null : reader.GetString(index);
                    var key = SourceSyncCodec.Key(table.Table, values);
                    var row = SourceSyncCodec.Hash(values);
                    if (!CryptographicOperations.FixedTimeEquals(key, reader.GetFieldValue<byte[]>(values.Length))
                        || !CryptographicOperations.FixedTimeEquals(row, reader.GetFieldValue<byte[]>(values.Length + 1)))
                        throw Conflict("Snapshot actual row/key hash verification failed.");
                    actual.Add(row);
                }
            }
            if (actual.Count != table.Count || !actual.Matches(expected)) throw Conflict("Snapshot count or multiset digest verification failed.");
            await ExecuteAsync(db, tx, "UPDATE source_sync.tables SET state='indexing' WHERE source_id=@source AND table_name=@table", ct,
                ("source", source), ("table", table.Table.Name));
        }
        else if (table.State == "indexing")
        {
            foreach (var (name, column, unique) in new[] {
                ("__ss_key", SourceSyncCodec.KeyColumn, table.Table.PrimaryKey.Length != 0), ("__ss_row", SourceSyncCodec.RowColumn, false) })
            {
                var exists = (int)(await ScalarAsync(db, tx, "SELECT COUNT(*) FROM sys.indexes WHERE object_id=@id AND name=@name", ct,
                    ("id", table.ObjectId), ("name", name)))!;
                if (exists != 0) continue;
                await using var build = Command(db, tx, "CREATE " + (unique ? "UNIQUE " : "") + "INDEX " + SourceSyncCodec.Quote(name)
                    + " ON " + Name(source, table.Table.Name) + "(" + SourceSyncCodec.Quote(column) + ") WITH(MAXDOP=2,SORT_IN_TEMPDB=OFF)");
                build.CommandTimeout = 0;
                await build.ExecuteNonQueryAsync(ct);
            }
            await OwnedAsync(db, tx, source, table with { State = "ready" }, ct);
            await ExecuteAsync(db, tx, "UPDATE source_sync.tables SET state='ready' WHERE source_id=@source AND table_name=@table", ct,
                ("source", source), ("table", table.Table.Name));
        }
        else throw Conflict("Invalid snapshot finalization phase.");
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task RecordFinalizationFailureAsync(string source, string code, CancellationToken ct)
    {
        await using var db = await OpenAsync(source, ct);
        await ExecuteAsync(db, null, "UPDATE source_sync.sources SET raw_error=@error,updated_at=SYSUTCDATETIME() WHERE source_id=@source AND state='finalizing'", ct,
            ("source", source), ("error", code));
    }
}

public sealed class SourceSyncFinalizer(IServiceScopeFactory scopes, IOptionsMonitor<SourceSyncOptions> options,
    ILogger<SourceSyncFinalizer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var progress = false;
            if (options.CurrentValue.Enabled)
            {
                foreach (var source in options.CurrentValue.Sources.Keys.ToArray())
                {
                    using var scope = scopes.CreateScope();
                    var store = scope.ServiceProvider.GetRequiredService<SourceSyncStore>();
                    try
                    {
                        progress |= await store.FinalizeNextAsync(source, stoppingToken);
                        progress |= await store.ProcessStagedBatchAsync(source, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception exception)
                    {
                        var fatal = exception is ConflictException conflict && !conflict.Message.StartsWith("Source sync is busy;", StringComparison.Ordinal)
                            || exception is BusinessException || exception is SqlException { Number: 2601 or 2627 };
                        var code = exception is SqlException sql ? "sql_" + Math.Abs((long)sql.Number) : fatal ? "raw_verification_failed" : "finalization_retry";
                        logger.LogWarning("Source finalization {Source}: {Code}", source, code);
                        if (fatal)
                        {
                            try { await store.RecordFinalizationFailureAsync(source, code, stoppingToken); }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                            catch { logger.LogWarning("Could not persist source finalization failure for {Source}", source); }
                        }
                    }
                }
            }
            await Task.Delay(progress ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
