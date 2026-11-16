using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using CorePlasma.Common.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace CorePlasma.API.SourceSync;

/// <summary>Dedicated SQL raw store. Its connection must never have native dbo write/DDL rights.</summary>
public sealed partial class SourceSyncStore(IOptionsMonitor<SourceSyncOptions> options) : ISourceSyncRawReader
{
    private const string Collation = "Latin1_General_100_BIN2";
    private sealed record Bound(Guid InstallationId, Guid Generation, string State, byte[] BeginHash, string ProjectionState, string? ErrorCode);
    private sealed record Registered(SourceSyncTable Table, int ObjectId, long Count, string State);

    public async Task<object> BeginAsync(string source, SourceSyncBegin body, CancellationToken ct)
    {
        SourceSyncCodec.Tables(body.Tables);
        if (body.InstallationId == Guid.Empty || body.Generation == Guid.Empty || string.IsNullOrEmpty(body.Snapshot)
            || body.Snapshot.Length > 100_000) throw new BusinessException("Invalid source snapshot identity.");
        var hash = SourceSyncCodec.Hash(new[] { body.InstallationId.ToString("D"), body.Generation.ToString("D"), body.Snapshot,
                body.Tables.Length.ToString(CultureInfo.InvariantCulture) }
            .Concat(body.Tables.SelectMany(table => new[] { table.Name, table.CatalogSignature,
                    table.Columns.Length.ToString(CultureInfo.InvariantCulture) }
                .Concat(table.Columns).Concat(new[] { table.PrimaryKey.Length.ToString(CultureInfo.InvariantCulture) }).Concat(table.PrimaryKey))));
        await using var db = await OpenAsync(source, ct);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(ct);
        await LockAsync(db, tx, source, ct);
        var bound = await BindingAsync(db, tx, source, ct);
        if (bound is not null)
        {
            if (bound.InstallationId != body.InstallationId) throw Conflict("Source installation cannot be replaced.");
            if (bound.Generation == body.Generation)
            {
                EqualHash(hash, bound.BeginHash);
                await tx.CommitAsync(ct);
                return new { source, body.InstallationId, body.Generation, status = bound.State };
            }
            if (bound.State != "seeding") throw Conflict("A ready source requires an explicit reviewed rebaseline.");
            var previous = await TablesAsync(db, tx, source, ct);
            foreach (var table in previous)
            {
                await OwnedAsync(db, tx, source, table, ct);
                await ExecuteAsync(db, tx, "DROP TABLE " + Name(source, table.Table.Name), ct);
            }
            await ExecuteAsync(db, tx, "DELETE FROM source_sync.chunks WHERE source_id=@source; DELETE FROM source_sync.tables WHERE source_id=@source;", ct, ("source", source));
            await ExecuteAsync(db, tx, "UPDATE source_sync.sources SET generation=@generation,snapshot=@snapshot,begin_hash=@hash,state='seeding',projection_state='pending',projection_error=NULL,updated_at=SYSUTCDATETIME() WHERE source_id=@source", ct,
                ("generation", body.Generation), ("snapshot", body.Snapshot), ("hash", hash), ("source", source));
        }
        else
        {
            await ExecuteAsync(db, tx, "INSERT INTO source_sync.sources(source_id,installation_id,generation,snapshot,begin_hash,state) VALUES(@source,@installation,@generation,@snapshot,@hash,'seeding')", ct,
                ("source", source), ("installation", body.InstallationId), ("generation", body.Generation), ("snapshot", body.Snapshot), ("hash", hash));
        }
        foreach (var table in body.Tables) await CreateAsync(db, tx, source, table, body, ct);
        await tx.CommitAsync(ct);
        return new { source, body.InstallationId, body.Generation, status = "seeding" };
    }

    public async Task<object> RowsAsync(string source, SourceSyncRows body, CancellationToken ct)
    {
        if (body.ChunkId == Guid.Empty || body.Rows is null || body.Rows.Length is < 1 or > SourceSyncCodec.SnapshotMaximumItems)
            throw new BusinessException("Snapshot chunks require 1 to 5000 rows and a chunk UUID.");
        await using var db = await OpenAsync(source, ct);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(ct);
        await LockAsync(db, tx, source, ct);
        var binding = await RequireAsync(db, tx, source, body.InstallationId, body.Generation, ct);
        var table = await TableAsync(db, tx, source, body.Table, ct);
        var rows = body.Rows.Select(row => SourceSyncCodec.Row(row, table.Table)).ToArray();
        var hash = SourceSyncCodec.Hash(new[] { body.Table, body.ChunkId.ToString("D") }.Concat(rows.SelectMany(row => row)));
        var previous = await ScalarAsync(db, tx, "SELECT payload_hash FROM source_sync.chunks WHERE source_id=@source AND generation=@generation AND chunk_id=@chunk", ct,
            ("source", source), ("generation", body.Generation), ("chunk", body.ChunkId));
        if (previous is byte[] recorded)
        {
            EqualHash(hash, recorded);
            await tx.CommitAsync(ct);
            return new { source, body.InstallationId, body.Generation, body.Table, body.ChunkId, rowCount = rows.Length.ToString(CultureInfo.InvariantCulture), status = "duplicate" };
        }
        if (binding.State != "seeding") throw Conflict("Source snapshot is already complete.");
        await OwnedAsync(db, tx, source, table, ct);
        await InsertSnapshotRowsAsync(db, tx, source, table.Table, rows, ct);
        var digest = new Digest();
        foreach (var row in rows) digest.Add(SourceSyncCodec.Hash(row));
        await ExecuteAsync(db, tx, "INSERT INTO source_sync.chunks(source_id,generation,chunk_id,table_name,payload_hash,row_count,digest_sum,digest_xor) VALUES(@source,@generation,@chunk,@table,@hash,@count,@sum,@xor); UPDATE source_sync.tables SET row_count=row_count+@count WHERE source_id=@source AND table_name=@table", ct,
            ("source", source), ("generation", body.Generation), ("chunk", body.ChunkId), ("table", body.Table), ("hash", hash), ("count", (long)rows.Length), ("sum", digest.Sum), ("xor", digest.Xor));
        await tx.CommitAsync(ct);
        return new { source, body.InstallationId, body.Generation, body.Table, body.ChunkId, rowCount = rows.Length.ToString(CultureInfo.InvariantCulture), status = "applied" };
    }

    public async Task<SourceSyncCompletionAck> CompleteAsync(string source, SourceSyncComplete body, CancellationToken ct)
    {
        if (body.Tables is null || body.Tables.Length is < 1 or > 256) throw new BusinessException("Invalid snapshot completion counts.");
        var expected = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var item in body.Tables)
        {
            if (item is null) throw new BusinessException("Invalid snapshot table count.");
            SourceSyncCodec.Identifier(item.Name);
            if (!expected.TryAdd(item.Name, SourceSyncCodec.DecimalId(item.RowCount, true)))
                throw new BusinessException("Snapshot table counts must be distinct.");
        }
        await using var db = await OpenAsync(source, ct);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(ct);
        await LockAsync(db, tx, source, ct);
        var binding = await RequireAsync(db, tx, source, body.InstallationId, body.Generation, ct);
        var tables = await TablesAsync(db, tx, source, ct);
        if (tables.Count != expected.Count) throw Conflict("Snapshot table counts differ from the catalog.");
        foreach (var table in tables)
        {
            var seededCount = (long)(await ScalarAsync(db, tx, "SELECT COALESCE(SUM(row_count),CONVERT(bigint,0)) FROM source_sync.chunks WHERE source_id=@source AND generation=@generation AND table_name=@table", ct,
                ("source", source), ("generation", body.Generation), ("table", table.Table.Name)))!;
            if (!expected.TryGetValue(table.Table.Name, out var count) || count != seededCount)
                throw Conflict("Snapshot row count does not match its received chunks.");
            await OwnedAsync(db, tx, source, table, ct);
            if (binding.State == "seeding" && table.Count != count) throw Conflict("Snapshot manifest count differs from its chunks.");
        }
        if (binding.State == "seeding")
            await ExecuteAsync(db, tx, "UPDATE source_sync.sources SET state='finalizing',raw_error=NULL,updated_at=SYSUTCDATETIME() WHERE source_id=@source", ct, ("source", source));
        await tx.CommitAsync(ct);
        return new(source, body.InstallationId, body.Generation, binding.State == "ready" ? "ready" : "finalizing");
    }

    public async Task<object> EventsAsync(string source, SourceSyncEvents body, CancellationToken ct)
    {
        if (body.BatchId == Guid.Empty || body.Events is null || body.Events.Length is < 1 or > SourceSyncCodec.MaximumItems)
            throw new BusinessException("Source batches require 1 to 1000 events and a batch UUID.");
        await using var db = await OpenAsync(source, ct);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(ct);
        await LockAsync(db, tx, source, ct);
        var binding = await RequireAsync(db, tx, source, body.InstallationId, body.Generation, ct);
        if (binding.State != "ready") throw Conflict("Source snapshot is incomplete.");
        var tables = (await TablesAsync(db, tx, source, ct)).ToDictionary(table => table.Table.Name, StringComparer.Ordinal);
        var hashes = new List<byte[]>();
        long previousId = 0;
        foreach (var item in body.Events)
        {
            if (item is null || item.Table is null || !tables.TryGetValue(item.Table, out var table)) throw new BusinessException("Event refers to an unknown source table.");
            var id = SourceSyncCodec.DecimalId(item.Id);
            if (id <= previousId) throw new BusinessException("Source batch event IDs must be strictly increasing.");
            previousId = id;
            hashes.Add(SourceSyncCodec.EventHash(item, table.Table));
        }
        var batchHash = SourceSyncCodec.Hash(hashes.Select(Convert.ToHexString));
        var existingBatch = await ScalarAsync(db, tx, "SELECT payload_hash FROM source_sync.batches WHERE source_id=@source AND installation_id=@installation AND batch_id=@batch", ct,
            ("source", source), ("installation", body.InstallationId), ("batch", body.BatchId));
        if (existingBatch is byte[] oldHash)
        {
            EqualHash(batchHash, oldHash);
            await tx.CommitAsync(ct);
            return EventAck(source, body, "duplicate");
        }
        if ((long)(await ScalarAsync(db, tx, "SELECT COUNT_BIG(*) FROM source_sync.event_jobs WHERE source_id=@source AND state<>'applied'", ct, ("source", source)))! != 0)
            throw Conflict("A multipart predecessor is pending; direct batches cannot bypass it.");
        var allDuplicate = await ApplyValidatedAsync(db, tx, source, body.InstallationId, body.Generation, body.Events, hashes, tables,
            new HashSet<string>(StringComparer.Ordinal), ct);
        await ExecuteAsync(db, tx, "INSERT INTO source_sync.batches(source_id,installation_id,batch_id,generation,payload_hash) VALUES(@source,@installation,@batch,@generation,@hash); UPDATE source_sync.sources SET updated_at=SYSUTCDATETIME() WHERE source_id=@source", ct,
            ("source", source), ("installation", body.InstallationId), ("batch", body.BatchId), ("generation", body.Generation), ("hash", batchHash));
        await tx.CommitAsync(ct);
        return EventAck(source, body, allDuplicate ? "duplicate" : "applied");
    }

    private static object EventAck(string source, SourceSyncEvents body, string status) =>
        new { source, body.InstallationId, body.Generation, body.BatchId, eventIds = body.Events.Select(item => item.Id).ToArray(), status };

    private static async Task<bool> ApplyValidatedAsync(SqlConnection db, SqlTransaction tx, string source, Guid installation, Guid generation,
        SourceSyncEvent[] events, List<byte[]> hashes, Dictionary<string, Registered> tables, HashSet<string> validated, CancellationToken ct)
    {
        var allDuplicate = true;
        for (var index = 0; index < events.Length; index++)
        {
            var item = events[index];
            var existing = await ScalarAsync(db, tx, "SELECT payload_hash FROM source_sync.events WHERE source_id=@source AND installation_id=@installation AND event_id=@id", ct,
                ("source", source), ("installation", installation), ("id", SourceSyncCodec.DecimalId(item.Id)));
            if (existing is byte[] receipt) { EqualHash(hashes[index], receipt); continue; }
            allDuplicate = false;
            var table = tables[item.Table];
            if (validated.Add(item.Table)) await OwnedAsync(db, tx, source, table, ct);
            await MutateAsync(db, tx, source, table.Table, item, ct);
            var countSql = item.Operation switch { "INSERT" => "row_count+1", "DELETE" => "row_count-1", "TRUNCATE" => "0", _ => "row_count" };
            await ExecuteAsync(db, tx, "UPDATE source_sync.tables SET row_count=" + countSql + " WHERE source_id=@source AND table_name=@table; IF EXISTS(SELECT 1 FROM source_sync.tables WHERE source_id=@source AND table_name=@table AND row_count<0) THROW 51000,'Invalid raw row count.',1; INSERT INTO source_sync.events(source_id,installation_id,generation,event_id,transaction_id,table_name,payload_hash,payload) VALUES(@source,@installation,@generation,@id,@transaction,@table,@hash,@payload)", ct,
                ("source", source), ("installation", installation), ("generation", generation), ("id", SourceSyncCodec.DecimalId(item.Id)),
                ("transaction", SourceSyncCodec.DecimalId(item.TransactionId)), ("table", item.Table), ("hash", hashes[index]), ("payload", JsonSerializer.Serialize(item, SourceSyncCodec.Json)));
        }
        return allDuplicate;
    }

    private static async Task MutateAsync(SqlConnection db, SqlTransaction tx, string source, SourceSyncTable table, SourceSyncEvent item, CancellationToken ct)
    {
        if (item.Operation == "TRUNCATE") { await ExecuteAsync(db, tx, "TRUNCATE TABLE " + Name(source, table.Name), ct); return; }
        if (item.Operation == "INSERT") { await InsertAsync(db, tx, source, table, SourceSyncCodec.Row(item.NewRow, table), ct); return; }
        var old = SourceSyncCodec.Row(item.OldRow, table);
        var key = SourceSyncCodec.Key(table, old);
        var hash = SourceSyncCodec.Hash(old);
        var predicate = " WHERE [__ss_key_hash]=@oldKey AND [__ss_row_hash]=@oldHash";
        // Hash indexes locate candidates, but stored hashes alone never prove the actual before-image.
        await using (var command = Command(db, tx, "SELECT TOP(1) " + Columns(table) + ",[__ss_key_hash],[__ss_row_hash] FROM " + Name(source, table.Name) + " WITH(UPDLOCK,HOLDLOCK)" + predicate,
            ("oldKey", key), ("oldHash", hash)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw Conflict("Source before-image was not found.");
            VerifyRow(reader, table, old);
        }
        if (item.Operation == "DELETE")
        {
            if (await ExecuteAsync(db, tx, "DELETE TOP(1) FROM " + Name(source, table.Name) + predicate, ct, ("oldKey", key), ("oldHash", hash)) != 1)
                throw Conflict("Source delete did not affect exactly one row.");
            return;
        }
        var row = SourceSyncCodec.Row(item.NewRow, table);
        var values = RowParameters(table, row).Concat(new[] { ("oldKey", (object?)key), ("oldHash", (object?)hash) }).ToArray();
        var assignments = table.Columns.Select((column, index) => SourceSyncCodec.Quote(column) + "=@v" + index)
            .Concat(new[] { "[__ss_key_hash]=@key", "[__ss_row_hash]=@row" });
        try
        {
            if (await ExecuteAsync(db, tx, "UPDATE TOP(1) " + Name(source, table.Name) + " SET " + string.Join(',', assignments) + predicate, ct, values) != 1)
                throw Conflict("Source update did not affect exactly one row.");
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        { throw Conflict("Source primary key update conflicts with an existing raw row."); }
        await VerifyStoredAsync(db, tx, source, table, row, ct);
    }

    private static async Task InsertAsync(SqlConnection db, SqlTransaction tx, string source, SourceSyncTable table, string?[] row, CancellationToken ct)
    {
        var sql = "INSERT INTO " + Name(source, table.Name) + " (" + Columns(table) + ",[__ss_key_hash],[__ss_row_hash]) VALUES("
            + string.Join(',', table.Columns.Select((_, index) => "@v" + index)) + ",@key,@row)";
        try
        {
            if (await ExecuteAsync(db, tx, sql, ct, RowParameters(table, row)) != 1) throw Conflict("Source insert did not affect exactly one row.");
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        { throw Conflict("Source primary key conflicts with an existing raw row."); }
        await VerifyStoredAsync(db, tx, source, table, row, ct);
    }

    private static async Task InsertSnapshotRowsAsync(SqlConnection db, SqlTransaction tx, string source, SourceSyncTable table, string?[][] rows, CancellationToken ct)
    {
        // All snapshot tables remain heaps until durable background verification.
        using var data = new DataTable { CaseSensitive = true, Locale = CultureInfo.InvariantCulture };
        foreach (var column in table.Columns) data.Columns.Add(column, typeof(string));
        data.Columns.Add(SourceSyncCodec.KeyColumn, typeof(byte[]));
        data.Columns.Add(SourceSyncCodec.RowColumn, typeof(byte[]));
        foreach (var row in rows)
        {
            var key = SourceSyncCodec.Key(table, row);
            data.Rows.Add(row.Select(value => (object?)value ?? DBNull.Value).Concat(new object[] { key, SourceSyncCodec.Hash(row) }).ToArray());
        }
        using (var bulk = new SqlBulkCopy(db, SqlBulkCopyOptions.KeepNulls, tx)
        {
            DestinationTableName = Name(source, table.Name), BatchSize = rows.Length, BulkCopyTimeout = 120
        })
        {
            foreach (DataColumn column in data.Columns) bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            try { await bulk.WriteToServerAsync(data, ct); }
            catch (SqlException exception) when (exception.Number is 2601 or 2627)
            { throw Conflict("Snapshot source primary key conflicts with an existing raw row."); }
        }
    }

    private static async Task VerifyStoredAsync(SqlConnection db, SqlTransaction tx, string source, SourceSyncTable table, string?[] row, CancellationToken ct)
    {
        await using var command = Command(db, tx, "SELECT TOP(1) " + Columns(table) + ",[__ss_key_hash],[__ss_row_hash] FROM " + Name(source, table.Name)
            + " WHERE [__ss_key_hash]=@key AND [__ss_row_hash]=@row", ("key", SourceSyncCodec.Key(table, row)), ("row", SourceSyncCodec.Hash(row)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw Conflict("Source after-image verification failed.");
        VerifyRow(reader, table, row);
    }

    private static void VerifyRow(SqlDataReader reader, SourceSyncTable table, string?[] expected)
    {
        var actual = Enumerable.Range(0, table.Columns.Length).Select(index => reader.IsDBNull(index) ? null : reader.GetString(index)).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal)
            || !CryptographicOperations.FixedTimeEquals(SourceSyncCodec.Key(table, actual), reader.GetFieldValue<byte[]>(table.Columns.Length))
            || !CryptographicOperations.FixedTimeEquals(SourceSyncCodec.Hash(actual), reader.GetFieldValue<byte[]>(table.Columns.Length + 1)))
            throw Conflict("Actual source row differs from its expected image or stored hashes.");
    }

    private static (string, object?)[] RowParameters(SourceSyncTable table, string?[] row) => row.Select((value, index) => ("v" + index, (object?)value))
        .Concat(new[] { ("key", (object?)SourceSyncCodec.Key(table, row)), ("row", (object?)SourceSyncCodec.Hash(row)) }).ToArray();

    private static async Task CreateAsync(SqlConnection db, SqlTransaction tx, string source, SourceSyncTable table, SourceSyncBegin begin, CancellationToken ct)
    {
        var qualified = Name(source, table.Name);
        if (await ScalarAsync(db, tx, "SELECT OBJECT_ID(@name)", ct, ("name", qualified)) is not null)
            throw Conflict("Refusing to adopt an existing unowned raw table.");
        var columns = table.Columns.Select(column => SourceSyncCodec.Quote(column) + " nvarchar(max) COLLATE " + Collation + " NULL");
        await ExecuteAsync(db, tx, "CREATE TABLE " + qualified + " (" + string.Join(',', columns)
            + ",[__ss_key_hash] binary(32) NOT NULL,[__ss_row_hash] binary(32) NOT NULL)", ct);
        await ExecuteAsync(db, tx, "DECLARE @owned nvarchar(256)=@owner; EXEC sys.sp_addextendedproperty @name=N'core_source_sync_owner',@value=@owned,@level0type=N'SCHEMA',@level0name=@schema,@level1type=N'TABLE',@level1name=@table; INSERT INTO source_sync.tables(source_id,table_name,metadata,object_id) VALUES(@source,@table,@metadata,OBJECT_ID(@qualified))", ct,
            ("owner", source + ":" + begin.InstallationId.ToString("D") + ":" + begin.Generation.ToString("D")), ("schema", SourceSyncCodec.Schema(source)),
            ("table", table.Name), ("source", source), ("metadata", JsonSerializer.Serialize(table, SourceSyncCodec.Json)), ("qualified", qualified));
    }

    private static async Task OwnedAsync(SqlConnection db, SqlTransaction tx, string source, Registered registered, CancellationToken ct)
    {
        var binding = await BindingAsync(db, tx, source, ct) ?? throw Conflict("Source binding is missing.");
        var expectedOwner = source + ":" + binding.InstallationId.ToString("D") + ":" + binding.Generation.ToString("D");
        var owner = await ScalarAsync(db, tx, "SELECT CONVERT(nvarchar(256),value) FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(@name) AND minor_id=0 AND name=N'core_source_sync_owner'", ct, ("name", Name(source, registered.Table.Name)));
        var objectId = await ScalarAsync(db, tx, "SELECT OBJECT_ID(@name,N'U')", ct, ("name", Name(source, registered.Table.Name)));
        if (owner is not string text || text != expectedOwner || objectId is not int id || id != registered.ObjectId)
            throw Conflict("Raw table ownership does not match its manifest.");
        await using var command = Command(db, tx, "SELECT name,TYPE_NAME(user_type_id),max_length,is_nullable,collation_name,is_computed,is_identity FROM sys.columns WHERE object_id=@id ORDER BY column_id", ("id", id));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var index = 0;
        while (await reader.ReadAsync(ct))
        {
            if (index < registered.Table.Columns.Length)
            {
                if (reader.GetString(0) != registered.Table.Columns[index] || reader.GetString(1) != "nvarchar" || reader.GetInt16(2) != -1
                    || !reader.GetBoolean(3) || reader.GetString(4) != Collation || reader.GetBoolean(5) || reader.GetBoolean(6)) throw Conflict("Raw SQL column schema drift detected.");
            }
            else
            {
                var expected = index == registered.Table.Columns.Length ? SourceSyncCodec.KeyColumn : SourceSyncCodec.RowColumn;
                if (index > registered.Table.Columns.Length + 1 || reader.GetString(0) != expected || reader.GetString(1) != "binary"
                    || reader.GetInt16(2) != 32 || reader.GetBoolean(3) || reader.GetBoolean(5) || reader.GetBoolean(6)) throw Conflict("Raw SQL hash columns differ from the manifest.");
            }
            index++;
        }
        if (index != registered.Table.Columns.Length + 2) throw Conflict("Raw SQL column count differs from the manifest.");
        await reader.CloseAsync();
        var sideEffects = (int)(await ScalarAsync(db, tx, "SELECT (SELECT COUNT(*) FROM sys.triggers WHERE parent_id=@id)+(SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=@id OR referenced_object_id=@id)+(SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=@id)+(SELECT COUNT(*) FROM sys.default_constraints WHERE parent_object_id=@id)", ct, ("id", id)))!;
        if (sideEffects != 0) throw Conflict("Raw SQL table must not have triggers, foreign keys or added constraints.");
        await using var indexes = Command(db, tx, "SELECT i.name,i.is_unique,i.is_disabled,c.name,ic.key_ordinal,ic.is_included_column,i.has_filter FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=@id AND i.index_id>0 ORDER BY i.name,ic.index_column_id", ("id", id));
        await using var indexReader = await indexes.ExecuteReaderAsync(ct);
        var seen = 0;
        while (await indexReader.ReadAsync(ct))
        {
            var indexName = indexReader.GetString(0);
            var unique = indexName == "__ss_key" && registered.Table.PrimaryKey.Length != 0;
            var column = indexName == "__ss_key" ? SourceSyncCodec.KeyColumn : SourceSyncCodec.RowColumn;
            if (indexName is not ("__ss_key" or "__ss_row") || indexReader.GetBoolean(1) != unique || indexReader.GetBoolean(2)
                || indexReader.GetString(3) != column || indexReader.GetByte(4) != 1 || indexReader.GetBoolean(5) || indexReader.GetBoolean(6))
                throw Conflict("Raw SQL indexes differ from the owned manifest.");
            seen++;
        }
        if (registered.State == "ready" && seen != 2 || registered.State == "seeding" && seen != 0)
            throw Conflict("Raw SQL indexes differ from their seeding/finalization phase.");
    }

    private async Task<SqlConnection> OpenAsync(string source, CancellationToken ct)
    {
        SourceSyncCodec.Schema(source);
        var settings = options.CurrentValue;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ConnectionString) || !settings.Sources.ContainsKey(source))
            throw new ForbiddenException("Source sync is not configured.");
        var db = new SqlConnection(settings.ConnectionString);
        try { await db.OpenAsync(ct); return db; }
        catch { await db.DisposeAsync(); throw; }
    }

    private static async Task LockAsync(SqlConnection db, SqlTransaction tx, string source, CancellationToken ct)
    {
        var result = await ScalarAsync(db, tx, "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; SELECT @result", ct, ("resource", "core-source-sync:" + source));
        if (result is not int value || value < 0) throw Conflict("Source sync is busy; retry the unchanged request.");
    }

    private static async Task<Bound?> BindingAsync(SqlConnection db, SqlTransaction? tx, string source, CancellationToken ct)
    {
        await using var command = Command(db, tx, "SELECT installation_id,generation,state,begin_hash,projection_state,raw_error FROM source_sync.sources WHERE source_id=@source", ("source", source));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new Bound(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetFieldValue<byte[]>(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)) : null;
    }

    private static async Task<Bound> RequireAsync(SqlConnection db, SqlTransaction tx, string source, Guid installation, Guid generation, CancellationToken ct)
    {
        var bound = await BindingAsync(db, tx, source, ct);
        if (bound is null || bound.InstallationId != installation || bound.Generation != generation)
            throw Conflict("Source generation/installation does not match its snapshot.");
        return bound;
    }

    private static async Task<List<Registered>> TablesAsync(SqlConnection db, SqlTransaction? tx, string source, CancellationToken ct)
    {
        await using var command = Command(db, tx, "SELECT metadata,object_id,row_count,state FROM source_sync.tables WHERE source_id=@source ORDER BY table_name", ("source", source));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var tables = new List<Registered>();
        while (await reader.ReadAsync(ct)) tables.Add(new Registered(JsonSerializer.Deserialize<SourceSyncTable>(reader.GetString(0), SourceSyncCodec.Json)
            ?? throw Conflict("Invalid raw table manifest."), reader.GetInt32(1), reader.GetInt64(2), reader.GetString(3)));
        return tables;
    }

    private static async Task<Registered> TableAsync(SqlConnection db, SqlTransaction tx, string source, string name, CancellationToken ct) =>
        (await TablesAsync(db, tx, source, ct)).SingleOrDefault(table => table.Table.Name == name) ?? throw new BusinessException("Unknown source table.");

    private static string Name(string source, string table) => SourceSyncCodec.Quote(SourceSyncCodec.Schema(source)) + "." + SourceSyncCodec.Quote(table);
    private static string Columns(SourceSyncTable table) => string.Join(',', table.Columns.Select(SourceSyncCodec.Quote));
    private static ConflictException Conflict(string message) => new(message);
    private static void EqualHash(byte[] actual, byte[] expected)
    {
        if (!CryptographicOperations.FixedTimeEquals(actual, expected)) throw Conflict("Immutable source delivery payload conflicts with its recorded receipt.");
    }

    private static SqlCommand Command(SqlConnection db, SqlTransaction? tx, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = new SqlCommand(sql, db, tx) { CommandTimeout = 120 };
        foreach (var (name, value) in parameters)
        {
            var parameter = value switch
            {
                null => new SqlParameter("@" + name, SqlDbType.NVarChar, -1) { Value = DBNull.Value },
                string text => new SqlParameter("@" + name, SqlDbType.NVarChar, -1) { Value = text },
                byte[] bytes => new SqlParameter("@" + name, SqlDbType.VarBinary, bytes.Length) { Value = bytes },
                _ => new SqlParameter("@" + name, value)
            };
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private static async Task<int> ExecuteAsync(SqlConnection db, SqlTransaction? tx, string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(db, tx, sql, parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ScalarAsync(SqlConnection db, SqlTransaction? tx, string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(db, tx, sql, parameters);
        var value = await command.ExecuteScalarAsync(ct);
        return value is DBNull ? null : value;
    }

    public async Task<SourceSyncState?> GetStateAsync(string source, CancellationToken cancellationToken)
    {
        await using var db = await OpenAsync(source, cancellationToken);
        var binding = await BindingAsync(db, null, source, cancellationToken);
        if (binding is null) return null;
        var tables = (int)(await ScalarAsync(db, null, "SELECT COUNT(*) FROM source_sync.tables WHERE source_id=@source", cancellationToken, ("source", source)))!;
        var pending = (long)(await ScalarAsync(db, null, "SELECT COUNT_BIG(*) FROM source_sync.events WHERE source_id=@source AND projection_state<>'applied'", cancellationToken, ("source", source)))!;
        var jobs = (long)(await ScalarAsync(db, null, "SELECT COUNT_BIG(*) FROM source_sync.event_jobs WHERE source_id=@source AND state<>'applied'", cancellationToken, ("source", source)))!;
        var error = binding.ErrorCode ?? await ScalarAsync(db, null, "SELECT TOP(1) error_code FROM source_sync.event_jobs WHERE source_id=@source AND state='failed' ORDER BY job_id", cancellationToken, ("source", source)) as string;
        return new SourceSyncState(source, binding.InstallationId, binding.Generation, binding.State, tables, pending, binding.ProjectionState, error, jobs);
    }

    public async Task<IReadOnlyList<SourceSyncTable>> GetTablesAsync(string source, CancellationToken cancellationToken)
    {
        await using var db = await OpenAsync(source, cancellationToken);
        return (await TablesAsync(db, null, source, cancellationToken)).Select(table => table.Table).ToArray();
    }

    public async IAsyncEnumerable<SourceSyncRawRow> ReadRowsAsync(string source, string table, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var db = await OpenAsync(source, cancellationToken);
        Registered registered;
        await using (var tx = (SqlTransaction)await db.BeginTransactionAsync(cancellationToken))
        {
            await LockAsync(db, tx, source, cancellationToken);
            var bound = await BindingAsync(db, tx, source, cancellationToken);
            if (bound?.State != "ready") throw Conflict("Raw source baseline is not ready for projection.");
            registered = await TableAsync(db, tx, source, table, cancellationToken);
            await OwnedAsync(db, tx, source, registered, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        // Baseline projections are eventually reconciled by immutable queued events;
        // never hold the ingestion application lock while native adapters process rows.
        await using var command = Command(db, null, "SELECT " + Columns(registered.Table) + ",[__ss_key_hash],[__ss_row_hash] FROM " + Name(source, table));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = Enumerable.Range(0, registered.Table.Columns.Length).Select(index => reader.IsDBNull(index) ? null : reader.GetString(index)).ToArray();
            VerifyRow(reader, registered.Table, values);
            var row = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var index = 0; index < registered.Table.Columns.Length; index++) row[registered.Table.Columns[index]] = values[index];
            yield return new SourceSyncRawRow(table, row);
        }
    }

    public async Task<IReadOnlyList<SourceSyncPendingEvent>> ReadPendingAsync(string source, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > SourceSyncCodec.MaximumItems) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var db = await OpenAsync(source, cancellationToken);
        await using var command = Command(db, null, "SELECT TOP(@limit) installation_id,generation,event_id,payload,projection_state FROM source_sync.events WHERE source_id=@source AND projection_state IN('pending','blocked') ORDER BY CASE projection_state WHEN 'pending' THEN 0 ELSE 1 END,event_id", ("limit", limit), ("source", source));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<SourceSyncPendingEvent>();
        while (await reader.ReadAsync(cancellationToken)) events.Add(new SourceSyncPendingEvent(source, reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2).ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Deserialize<SourceSyncEvent>(reader.GetString(3), SourceSyncCodec.Json) ?? throw Conflict("Invalid stored source event."), reader.GetString(4)));
        return events;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<SourceSyncRawRow>>> ReadProjectionSnapshotAsync(string source, string[] tables, CancellationToken cancellationToken)
    {
        if (tables is null || tables.Length is < 1 or > 32 || tables.Distinct(StringComparer.Ordinal).Count() != tables.Length)
            throw new ArgumentException("Select 1 to 32 distinct registered projection tables.");
        await using var db = await OpenAsync(source, cancellationToken);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, tx, source, cancellationToken);
        var bound = await BindingAsync(db, tx, source, cancellationToken);
        if (bound?.State != "ready") throw Conflict("Raw source baseline is not ready for projection.");
        var catalog = (await TablesAsync(db, tx, source, cancellationToken)).ToDictionary(table => table.Table.Name, StringComparer.Ordinal);
        var result = new Dictionary<string, IReadOnlyList<SourceSyncRawRow>>(StringComparer.Ordinal);
        foreach (var name in tables)
        {
            if (!catalog.TryGetValue(name, out var registered)) throw new ArgumentException("Unknown projection table.");
            await OwnedAsync(db, tx, source, registered, cancellationToken);
            await using var command = Command(db, tx, "SELECT " + Columns(registered.Table) + ",[__ss_key_hash],[__ss_row_hash] FROM " + Name(source, name));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<SourceSyncRawRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var values = Enumerable.Range(0, registered.Table.Columns.Length).Select(index => reader.IsDBNull(index) ? null : reader.GetString(index)).ToArray();
                VerifyRow(reader, registered.Table, values);
                var row = new Dictionary<string, string?>(StringComparer.Ordinal);
                for (var index = 0; index < registered.Table.Columns.Length; index++) row[registered.Table.Columns[index]] = values[index];
                rows.Add(new SourceSyncRawRow(name, row));
            }
            result.Add(name, rows);
        }
        await tx.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<SourceSyncRawRow?> ReadRowAsync(string source, string table, IReadOnlyDictionary<string, string?> primaryKey, CancellationToken cancellationToken)
    {
        await using var db = await OpenAsync(source, cancellationToken);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, tx, source, cancellationToken);
        var bound = await BindingAsync(db, tx, source, cancellationToken);
        if (bound?.State != "ready") throw Conflict("Raw source baseline is not ready for projection.");
        var registered = await TableAsync(db, tx, source, table, cancellationToken);
        if (registered.Table.PrimaryKey.Length == 0 || primaryKey.Count != registered.Table.PrimaryKey.Length
            || registered.Table.PrimaryKey.Any(column => !primaryKey.TryGetValue(column, out var value) || value is null)
            || primaryKey.Keys.Any(column => !registered.Table.PrimaryKey.Contains(column, StringComparer.Ordinal)))
            throw new ArgumentException("Provide the exact non-null registered primary key.");
        await OwnedAsync(db, tx, source, registered, cancellationToken);
        await using var command = Command(db, tx, "SELECT " + Columns(registered.Table) + ",[__ss_key_hash],[__ss_row_hash] FROM " + Name(source, table) + " WHERE [__ss_key_hash]=@key",
            ("key", SourceSyncCodec.Hash(registered.Table.PrimaryKey.Select(column => primaryKey[column]))));
        SourceSyncRawRow? result = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                var values = Enumerable.Range(0, registered.Table.Columns.Length).Select(index => reader.IsDBNull(index) ? null : reader.GetString(index)).ToArray();
                VerifyRow(reader, registered.Table, values);
                var row = registered.Table.Columns.Select((column, index) => (column, values[index])).ToDictionary(pair => pair.column, pair => pair.Item2, StringComparer.Ordinal);
                if (registered.Table.PrimaryKey.Any(column => row[column] != primaryKey[column])) throw Conflict("Raw primary key hash collision detected.");
                result = new SourceSyncRawRow(table, row);
                if (await reader.ReadAsync(cancellationToken)) throw Conflict("Raw primary key is not unique.");
            }
        }
        await tx.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<SourceSyncRawPage> ReadPageAsync(string source, string table, string? afterKey, int limit,
        IReadOnlyDictionary<string, string?>? exactFilters, string? search, string[]? searchColumns, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 200 || search?.Length > 200 || exactFilters?.Count > 8)
            throw new BusinessException("Invalid source page size or filters.");
        byte[]? cursor = null;
        if (afterKey is not null)
        {
            try { cursor = Convert.FromBase64String(afterKey); }
            catch (FormatException) { throw new BusinessException("Invalid source page cursor."); }
            if (cursor.Length != 32 || Convert.ToBase64String(cursor) != afterKey) throw new BusinessException("Invalid source page cursor.");
        }
        await using var db = await OpenAsync(source, cancellationToken);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, tx, source, cancellationToken);
        if ((await BindingAsync(db, tx, source, cancellationToken))?.State != "ready") throw Conflict("Raw source baseline is not ready for browsing.");
        var registered = await TableAsync(db, tx, source, table, cancellationToken);
        var keyless = registered.Table.PrimaryKey.Length == 0;
        await OwnedAsync(db, tx, source, registered, cancellationToken);
        var predicates = new List<string>();
        var parameters = new List<(string, object?)> { ("take", limit + 1) };
        if (cursor is not null) { predicates.Add("[__ss_key_hash]>@cursor"); parameters.Add(("cursor", cursor)); }
        if (exactFilters is not null)
        {
            foreach (var (column, value) in exactFilters)
            {
                if (!registered.Table.Columns.Contains(column, StringComparer.Ordinal) || value?.Length > 200)
                    throw new BusinessException("Unknown or oversized source page filter.");
                var name = "f" + parameters.Count;
                predicates.Add(value is null ? SourceSyncCodec.Quote(column) + " IS NULL"
                    : "(" + SourceSyncCodec.Quote(column) + "=@" + name + " AND DATALENGTH(" + SourceSyncCodec.Quote(column) + ")=DATALENGTH(@" + name + "))");
                if (value is not null) parameters.Add((name, value));
            }
        }
        if (!string.IsNullOrEmpty(search))
        {
            if (searchColumns is null || searchColumns.Length is < 1 or > 8
                || searchColumns.Distinct(StringComparer.Ordinal).Count() != searchColumns.Length
                || searchColumns.Any(column => !registered.Table.Columns.Contains(column, StringComparer.Ordinal)))
                throw new BusinessException("Unknown source search columns.");
            var escaped = search.Replace("~", "~~", StringComparison.Ordinal).Replace("%", "~%", StringComparison.Ordinal)
                .Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal);
            predicates.Add("(" + string.Join(" OR ", searchColumns.Select(column => SourceSyncCodec.Quote(column)
                + " COLLATE Latin1_General_100_CI_AI LIKE @search ESCAPE N'~'")) + ")");
            parameters.Add(("search", "%" + escaped + "%"));
        }
        var where = predicates.Count == 0 ? "" : " WHERE " + string.Join(" AND ", predicates);
        var sql = "SELECT TOP(@take) " + Columns(registered.Table) + ",[__ss_key_hash],[__ss_row_hash] FROM " + Name(source, table)
            + where + " ORDER BY [__ss_key_hash]";
        if (keyless)
        {
            // A keyless mirror is a multiset. Page distinct complete-row hashes
            // and retain each group's multiplicity instead of losing duplicates
            // at a cursor boundary or fabricating source IDs.
            var values = string.Join(',', registered.Table.Columns.Select(column => "[__ss_values]." + SourceSyncCodec.Quote(column)));
            sql = "SELECT TOP(@take) " + values + ",[__ss_values].[__ss_key_hash],[__ss_values].[__ss_row_hash],"
                + "[__ss_groups].[__ss_occurrences],[__ss_groups].[__ss_invalid_hashes] FROM (SELECT [__ss_key_hash],"
                + "COUNT_BIG(*) AS [__ss_occurrences],MAX(CASE WHEN [__ss_key_hash]<>[__ss_row_hash] THEN 1 ELSE 0 END) AS [__ss_invalid_hashes] FROM "
                + Name(source, table) + where + " GROUP BY [__ss_key_hash]) AS [__ss_groups] CROSS APPLY (SELECT TOP(1) "
                + Columns(registered.Table) + ",[__ss_key_hash],[__ss_row_hash] FROM " + Name(source, table)
                + " WHERE [__ss_key_hash]=[__ss_groups].[__ss_key_hash]) AS [__ss_values] ORDER BY [__ss_groups].[__ss_key_hash]";
        }
        var result = new List<SourceSyncRawRow>();
        string? lastCursor = null;
        var hasMore = false;
        await using (var command = Command(db, tx, sql, parameters.ToArray()))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (result.Count == limit) { hasMore = true; break; }
                var values = Enumerable.Range(0, registered.Table.Columns.Length).Select(index => reader.IsDBNull(index) ? null : reader.GetString(index)).ToArray();
                VerifyRow(reader, registered.Table, values);
                var occurrences = keyless ? reader.GetInt64(registered.Table.Columns.Length + 2) : 1;
                if (occurrences < 1 || keyless && reader.GetInt32(registered.Table.Columns.Length + 3) != 0)
                    throw Conflict("Keyless source group count or row hashes are invalid.");
                result.Add(new SourceSyncRawRow(table, registered.Table.Columns.Select((column, index) => (column, values[index]))
                    .ToDictionary(pair => pair.column, pair => pair.Item2, StringComparer.Ordinal), occurrences));
                lastCursor = Convert.ToBase64String(reader.GetFieldValue<byte[]>(registered.Table.Columns.Length));
            }
        }
        await tx.CommitAsync(cancellationToken);
        return new SourceSyncRawPage(result, hasMore ? lastCursor : null);
    }

    public async Task SetProjectionStateAsync(string source, Guid installationId, string eventId, string state, string? errorCode, CancellationToken cancellationToken)
    {
        ValidateProjection(state, errorCode);
        await using var db = await OpenAsync(source, cancellationToken);
        if (await ExecuteAsync(db, null, "UPDATE source_sync.events SET projection_state=@state,projection_error=@error WHERE source_id=@source AND installation_id=@installation AND event_id=@id", cancellationToken,
            ("state", state), ("error", errorCode), ("source", source), ("installation", installationId), ("id", SourceSyncCodec.DecimalId(eventId))) != 1)
            throw Conflict("Projection receipt does not exist.");
    }

    public async Task SetBaselineProjectionStateAsync(string source, Guid generation, string state, string? errorCode, CancellationToken cancellationToken)
    {
        ValidateProjection(state, errorCode);
        await using var db = await OpenAsync(source, cancellationToken);
        if (await ExecuteAsync(db, null, "UPDATE source_sync.sources SET projection_state=@state,projection_error=@error WHERE source_id=@source AND generation=@generation AND state='ready'", cancellationToken,
            ("state", state), ("error", errorCode), ("source", source), ("generation", generation)) != 1)
            throw Conflict("Projection baseline generation is not ready.");
    }

    private static void ValidateProjection(string state, string? errorCode)
    {
        if (state is not ("pending" or "applied" or "blocked" or "unsupported") || errorCode is not null
            && (errorCode.Length is < 1 or > 80 || errorCode.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_'))))
            throw new ArgumentException("Invalid projection state/error code.");
    }
}
