using System.Globalization;
using System.Text.Json;
using CorePlasma.Common.Exceptions;
using Microsoft.Data.SqlClient;

namespace CorePlasma.API.SourceSync;

public sealed partial class SourceSyncStore
{
    private sealed record EventJob(Guid Installation, Guid Generation, Guid Batch, string State, int? PartCount, long? EventCount, byte[]? Hash, string? ErrorCode);

    public async Task<object> StagePartAsync(string source, SourceSyncEventPart body, CancellationToken ct)
    {
        if (body.BatchId == Guid.Empty || body.PartIndex < 0 || body.Events is null || body.Events.Length is < 1 or > SourceSyncCodec.MaximumItems)
            throw new BusinessException("Event parts require a batch UUID, nonnegative index and 1 to 1000 events.");
        await using var db = await OpenAsync(source, ct);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(ct);
        await LockAsync(db, tx, source, ct);
        if ((await RequireAsync(db, tx, source, body.InstallationId, body.Generation, ct)).State != "ready") throw Conflict("Source snapshot is incomplete.");
        var tables = (await TablesAsync(db, tx, source, ct)).ToDictionary(item => item.Table.Name, StringComparer.Ordinal);
        var hashes = ValidateEvents(body.Events, tables);
        var hash = SourceSyncCodec.Hash(hashes.Select(Convert.ToHexString));
        var job = await JobAsync(db, tx, source, body.BatchId, ct);
        if (job is null)
            await ExecuteAsync(db, tx, "INSERT INTO source_sync.event_jobs(source_id,installation_id,batch_id,generation) VALUES(@source,@installation,@batch,@generation)", ct,
                ("source", source), ("installation", body.InstallationId), ("batch", body.BatchId), ("generation", body.Generation));
        else if (job.Installation != body.InstallationId || job.Generation != body.Generation) throw Conflict("Multipart batch binding conflicts.");
        var previous = await ScalarAsync(db, tx, "SELECT payload_hash FROM source_sync.event_parts WHERE source_id=@source AND installation_id=@installation AND batch_id=@batch AND part_index=@part", ct,
            ("source", source), ("installation", body.InstallationId), ("batch", body.BatchId), ("part", body.PartIndex));
        var disposition = "staged";
        if (previous is byte[] recorded) { EqualHash(hash, recorded); disposition = "duplicate"; }
        else
        {
            if (job is not null && job.State != "receiving") throw Conflict("A committed multipart batch cannot accept new parts.");
            await ExecuteAsync(db, tx, "INSERT INTO source_sync.event_parts(source_id,installation_id,batch_id,part_index,payload_hash,event_count,first_event_id,last_event_id,payload) VALUES(@source,@installation,@batch,@part,@hash,@count,@first,@last,@payload)", ct,
                ("source", source), ("installation", body.InstallationId), ("batch", body.BatchId), ("part", body.PartIndex), ("hash", hash), ("count", body.Events.Length),
                ("first", SourceSyncCodec.DecimalId(body.Events[0].Id)), ("last", SourceSyncCodec.DecimalId(body.Events[^1].Id)), ("payload", JsonSerializer.Serialize(body.Events, SourceSyncCodec.Json)));
        }
        await tx.CommitAsync(ct);
        return new { source, body.InstallationId, body.Generation, body.BatchId, body.PartIndex, eventIds = body.Events.Select(item => item.Id).ToArray(), status = disposition };
    }

    public async Task<object> CommitEventsAsync(string source, SourceSyncEventCommit body, CancellationToken ct)
    {
        if (body.BatchId == Guid.Empty || body.PartCount < 1) throw new BusinessException("Invalid multipart batch commit.");
        var expectedCount = SourceSyncCodec.DecimalId(body.EventCount);
        await using var db = await OpenAsync(source, ct);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(ct);
        await LockAsync(db, tx, source, ct);
        await RequireAsync(db, tx, source, body.InstallationId, body.Generation, ct);
        var job = await JobAsync(db, tx, source, body.BatchId, ct) ?? throw Conflict("Multipart batch has no staged parts.");
        if (job.Installation != body.InstallationId || job.Generation != body.Generation) throw Conflict("Multipart batch binding conflicts.");
        long count = 0, last = 0;
        var index = 0;
        var hashes = new List<string>();
        await using (var command = Command(db, tx, "SELECT part_index,event_count,first_event_id,last_event_id,payload_hash FROM source_sync.event_parts WHERE source_id=@source AND installation_id=@installation AND batch_id=@batch ORDER BY part_index",
            ("source", source), ("installation", body.InstallationId), ("batch", body.BatchId)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                if (reader.GetInt32(0) != index || reader.GetInt64(2) <= last) throw Conflict("Multipart parts are missing or not globally ordered.");
                index++; count = checked(count + reader.GetInt32(1)); last = reader.GetInt64(3);
                hashes.Add(Convert.ToHexString(reader.GetFieldValue<byte[]>(4)));
            }
        }
        if (index != body.PartCount || count != expectedCount) throw Conflict("Multipart commit count differs from its staged parts.");
        var hash = SourceSyncCodec.Hash(hashes);
        if (job.Hash is not null)
        {
            EqualHash(hash, job.Hash);
            if (job.PartCount != body.PartCount || job.EventCount != expectedCount) throw Conflict("Immutable multipart commit conflicts.");
        }
        else
            await ExecuteAsync(db, tx, "UPDATE source_sync.event_jobs SET state='queued',part_count=@parts,event_count=@count,payload_hash=@hash,updated_at=SYSUTCDATETIME() WHERE source_id=@source AND installation_id=@installation AND batch_id=@batch AND state='receiving'", ct,
                ("parts", body.PartCount), ("count", expectedCount), ("hash", hash), ("source", source), ("installation", body.InstallationId), ("batch", body.BatchId));
        await tx.CommitAsync(ct);
        return new { source, body.InstallationId, body.Generation, body.BatchId, eventCount = body.EventCount, partCount = body.PartCount, status = job.State == "receiving" ? "queued" : job.State };
    }

    public async Task<object> GetEventJobAsync(string source, Guid batch, int? partIndex, CancellationToken ct)
    {
        if (batch == Guid.Empty || partIndex < 0) throw new BusinessException("Invalid multipart batch/part identity.");
        await using var db = await OpenAsync(source, ct);
        var job = await JobAsync(db, null, source, batch, ct) ?? throw new NotFoundException("Source batch not found.");
        if (partIndex is null) return new { source, installationId = job.Installation, generation = job.Generation, batchId = batch,
            status = job.State, eventCount = job.EventCount?.ToString(CultureInfo.InvariantCulture), partCount = job.PartCount, errorCode = job.ErrorCode };
        var payload = await ScalarAsync(db, null, "SELECT payload FROM source_sync.event_parts WHERE source_id=@source AND installation_id=@installation AND batch_id=@batch AND part_index=@part", ct,
            ("source", source), ("installation", job.Installation), ("batch", batch), ("part", partIndex.Value));
        if (payload is not string json) throw new NotFoundException("Source batch part not found.");
        var items = JsonSerializer.Deserialize<SourceSyncEvent[]>(json, SourceSyncCodec.Json) ?? throw Conflict("Invalid stored multipart data.");
        return new { source, installationId = job.Installation, generation = job.Generation, batchId = batch, partIndex = partIndex.Value,
            eventIds = items.Select(item => item.Id).ToArray(), status = job.State, errorCode = job.ErrorCode };
    }

    private static async Task<EventJob?> JobAsync(SqlConnection db, SqlTransaction? tx, string source, Guid batch, CancellationToken ct)
    {
        await using var command = Command(db, tx, "SELECT installation_id,generation,batch_id,state,part_count,event_count,payload_hash,error_code FROM source_sync.event_jobs WHERE source_id=@source AND batch_id=@batch", ("source", source), ("batch", batch));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5), reader.IsDBNull(6) ? null : reader.GetFieldValue<byte[]>(6), reader.IsDBNull(7) ? null : reader.GetString(7)) : null;
    }

    private static List<byte[]> ValidateEvents(SourceSyncEvent[] items, Dictionary<string, Registered> tables)
    {
        var hashes = new List<byte[]>();
        long last = 0;
        foreach (var item in items)
        {
            if (item is null || item.Table is null || !tables.TryGetValue(item.Table, out var table)) throw new BusinessException("Event refers to an unknown source table.");
            var id = SourceSyncCodec.DecimalId(item.Id);
            if (id <= last) throw new BusinessException("Source event IDs must be strictly increasing.");
            last = id; hashes.Add(SourceSyncCodec.EventHash(item, table.Table));
        }
        return hashes;
    }

    /// <summary>Applies a whole closed source transaction prefix atomically, independent of HTTP/body-size limits.</summary>
    public async Task<bool> ProcessStagedBatchAsync(string source, CancellationToken ct)
    {
        await using var db = await OpenAsync(source, ct);
        Guid? batch = null;
        try
        {
            await using var tx = (SqlTransaction)await db.BeginTransactionAsync(ct);
            await LockAsync(db, tx, source, ct);
            var binding = await BindingAsync(db, tx, source, ct);
            if (binding?.State != "ready") return false;
            var next = await ScalarAsync(db, tx, "SELECT TOP(1) batch_id FROM source_sync.event_jobs WHERE source_id=@source AND state<>'applied' ORDER BY job_id", ct, ("source", source));
            if (next is not Guid id) return false;
            batch = id;
            var job = await JobAsync(db, tx, source, id, ct) ?? throw Conflict("Multipart job disappeared.");
            if (job.State != "queued") return false; // An incomplete/failed predecessor must not be bypassed.
            if (job.Installation != binding.InstallationId || job.Generation != binding.Generation) throw Conflict("Queued source batch generation changed.");
            if (await ScalarAsync(db, tx, "SELECT payload_hash FROM source_sync.batches WHERE source_id=@source AND installation_id=@installation AND batch_id=@batch", ct,
                ("source", source), ("installation", job.Installation), ("batch", id)) is not null) throw Conflict("Multipart batch ID conflicts with an existing direct batch.");
            var tables = (await TablesAsync(db, tx, source, ct)).ToDictionary(item => item.Table.Name, StringComparer.Ordinal);
            var validated = new HashSet<string>(StringComparer.Ordinal);
            long appliedCount = 0, last = 0;
            var partHashes = new List<string>();
            for (var part = 0; part < job.PartCount; part++)
            {
                string payload; byte[] recorded;
                await using (var command = Command(db, tx, "SELECT payload,payload_hash FROM source_sync.event_parts WHERE source_id=@source AND installation_id=@installation AND batch_id=@batch AND part_index=@part",
                    ("source", source), ("installation", job.Installation), ("batch", id), ("part", part)))
                await using (var reader = await command.ExecuteReaderAsync(ct))
                {
                    if (!await reader.ReadAsync(ct)) throw Conflict("Committed multipart batch lost a part.");
                    payload = reader.GetString(0); recorded = reader.GetFieldValue<byte[]>(1);
                }
                var items = JsonSerializer.Deserialize<SourceSyncEvent[]>(payload, SourceSyncCodec.Json) ?? throw Conflict("Invalid staged source batch payload.");
                var hashes = ValidateEvents(items, tables);
                var partHash = SourceSyncCodec.Hash(hashes.Select(Convert.ToHexString));
                EqualHash(partHash, recorded); partHashes.Add(Convert.ToHexString(partHash));
                if (SourceSyncCodec.DecimalId(items[0].Id) <= last) throw Conflict("Committed multipart event order changed.");
                last = SourceSyncCodec.DecimalId(items[^1].Id);
                await ApplyValidatedAsync(db, tx, source, job.Installation, job.Generation, items, hashes, tables, validated, ct);
                appliedCount = checked(appliedCount + items.Length);
            }
            if (appliedCount != job.EventCount || job.Hash is null) throw Conflict("Committed multipart event count changed.");
            EqualHash(SourceSyncCodec.Hash(partHashes), job.Hash);
            await ExecuteAsync(db, tx, "INSERT INTO source_sync.batches(source_id,installation_id,batch_id,generation,payload_hash) VALUES(@source,@installation,@batch,@generation,@hash); UPDATE source_sync.event_jobs SET state='applied',updated_at=SYSUTCDATETIME() WHERE source_id=@source AND installation_id=@installation AND batch_id=@batch; UPDATE source_sync.sources SET updated_at=SYSUTCDATETIME() WHERE source_id=@source", ct,
                ("source", source), ("installation", job.Installation), ("batch", id), ("generation", job.Generation), ("hash", job.Hash));
            await tx.CommitAsync(ct);
            return true;
        }
        catch (Exception exception) when (batch is not null && (exception is AppException || exception is JsonException || exception is SqlException { Number: 2601 or 2627 }))
        {
            // The failed SQL transaction has been disposed/rolled back before this durable diagnostic.
            var code = exception is SqlException ? "raw_primary_key_conflict" : "raw_batch_validation_failed";
            await ExecuteAsync(db, null, "UPDATE source_sync.event_jobs SET state='failed',error_code=@error,updated_at=SYSUTCDATETIME() WHERE source_id=@source AND batch_id=@batch AND state='queued'", ct,
                ("source", source), ("batch", batch.Value), ("error", code));
            return false;
        }
    }
}
