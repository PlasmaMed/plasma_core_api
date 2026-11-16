using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CorePlasma.API.SourceSync;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.API.SourceBusiness;

/// <summary>Idempotent source-owned records. Raw data remains authoritative; workflows are never invented here.</summary>
public sealed class SourceBusinessProjector(ApplicationDbContext context, ISourceSyncRawReader raw) : ISourceBusinessProjector
{
    public async Task<BusinessProjectionResult> ProjectBaselineAsync(string source, Guid installationId, Guid generation, CancellationToken cancellationToken)
    {
        var manifests = (await raw.GetTablesAsync(source, cancellationToken)).ToDictionary(x => x.Name, StringComparer.Ordinal);
        var catalogHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Policy = "business_v1",
            Tables = SourceBusinessCatalog.All.Where(x => x.Source == source && x.Native && manifests.ContainsKey(x.Table))
                .Select(x => new { x.Table, manifests[x.Table].CatalogSignature }).OrderBy(x => x.Table).ToArray() })));
        var checkpoint = await context.CoreBusinessProjectionStates.SingleOrDefaultAsync(x => x.Source == source, cancellationToken);
        if (checkpoint is not null && checkpoint.InstallationId == installationId && checkpoint.Generation == generation
            && checkpoint.CatalogHash == catalogHash) return new("applied");
        foreach (var table in SourceBusinessCatalog.All.Where(x => x.Source == source && x.Native))
        {
            if (!manifests.TryGetValue(table.Table, out var manifest)) continue;
            if (manifest.PrimaryKey.Length == 0) return new("blocked", "business_primary_key_missing");
            await ReconcileAsync(table, manifest, installationId, generation, null, cancellationToken);
        }
        if (checkpoint is null) { checkpoint = new CoreBusinessProjectionState { Source = source }; context.CoreBusinessProjectionStates.Add(checkpoint); }
        checkpoint.InstallationId = installationId; checkpoint.Generation = generation; checkpoint.CatalogHash = catalogHash;
        checkpoint.CompletedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(checkpoint).State = EntityState.Detached;
        return new("applied");
    }

    public async Task<BusinessProjectionResult> ProjectEventAsync(SourceSyncPendingEvent sourceEvent, CancellationToken cancellationToken)
    {
        var table = SourceBusinessCatalog.Find(sourceEvent.Source, sourceEvent.Event.Table);
        if (table is null || !table.Native) return new("unsupported");
        var manifest = (await raw.GetTablesAsync(sourceEvent.Source, cancellationToken)).SingleOrDefault(x => x.Name == table.Table);
        if (manifest is null || manifest.PrimaryKey.Length == 0) return new("blocked", "business_primary_key_missing");
        if (sourceEvent.Event.Operation == "TRUNCATE")
        {
            await ReconcileAsync(table, manifest, sourceEvent.InstallationId, sourceEvent.Generation, sourceEvent.EventId, cancellationToken);
            return new("applied");
        }
        var keys = new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.Ordinal);
        foreach (var image in new[] { sourceEvent.Event.OldRow, sourceEvent.Event.NewRow })
        {
            if (image.ValueKind == JsonValueKind.Null) continue;
            var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(image.GetRawText())!;
            var key = manifest.PrimaryKey.ToDictionary(column => column, column => values[column], StringComparer.Ordinal);
            keys[KeyHash(manifest, key)] = key;
        }
        foreach (var (hash, key) in keys)
        {
            // Coalesce to the verified current raw row; late retries cannot resurrect a deleted older image.
            var latest = await raw.ReadRowAsync(sourceEvent.Source, table.Table, key, cancellationToken);
            var existing = await context.CoreBusinessRecords.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Source == table.Source
                && x.SourceTable == table.Table && x.SourceKeyHash == hash, cancellationToken);
            if (latest is null)
            {
                if (existing is not null) { Bind(existing, sourceEvent.InstallationId); existing.IsDeleted = true;
                    existing.LastRawEventId = sourceEvent.EventId; existing.UpdatedDate = DateTime.UtcNow; }
            }
            else await UpsertAsync(table, manifest, latest.Values, sourceEvent.InstallationId, sourceEvent.Generation,
                Guid.Empty, sourceEvent.EventId, existing, cancellationToken);
        }
        await context.SaveChangesAsync(cancellationToken);
        DetachOwnRecords();
        return new("applied");
    }

    private async Task ReconcileAsync(SourceBusinessTable table, SourceSyncTable manifest, Guid installation, Guid generation,
        string? eventId, CancellationToken cancellationToken)
    {
        var run = Guid.NewGuid();
        var batch = 0;
        await foreach (var row in raw.ReadRowsAsync(table.Source, table.Table, cancellationToken))
        {
            await UpsertAsync(table, manifest, row.Values, installation, generation, run, eventId, null, cancellationToken);
            if (++batch % 200 == 0) { await context.SaveChangesAsync(cancellationToken); DetachOwnRecords(); }
        }
        await context.SaveChangesAsync(cancellationToken);
        DetachOwnRecords();
        // Only tombstone after the entire read succeeds. An interrupted baseline never hides unvisited records.
        await context.CoreBusinessRecords.IgnoreQueryFilters().Where(x => x.Source == table.Source && x.SourceTable == table.Table
            && x.InstallationId == installation && x.LastSeenRunId != run && !x.IsDeleted)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.IsDeleted, true)
                .SetProperty(x => x.UpdatedDate, DateTime.UtcNow), cancellationToken);
    }

    private async Task UpsertAsync(SourceBusinessTable table, SourceSyncTable manifest, IReadOnlyDictionary<string, string?> values,
        Guid installation, Guid generation, Guid run, string? eventId, CoreBusinessRecord? record, CancellationToken cancellationToken)
    {
        var hash = KeyHash(manifest, values);
        record ??= context.CoreBusinessRecords.Local.FirstOrDefault(x => x.Source == table.Source && x.SourceTable == table.Table && x.SourceKeyHash == hash)
            ?? await context.CoreBusinessRecords.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Source == table.Source
                && x.SourceTable == table.Table && x.SourceKeyHash == hash, cancellationToken);
        var safe = SourceBusinessCatalog.Sanitize(table.Table, values);
        var key = manifest.PrimaryKey.ToDictionary(column => column, column => values[column], StringComparer.Ordinal);
        var keyJson = JsonSerializer.Serialize(key);
        if (record is null)
        {
            record = new CoreBusinessRecord { Source = table.Source, SourceTable = table.Table, Domain = table.Domain,
                InstallationId = installation, SourceKeyHash = hash, ExternalKeyJson = keyJson, CreatedDate = DateTime.UtcNow };
            context.CoreBusinessRecords.Add(record);
        }
        Bind(record, installation);
        if (record.ExternalKeyJson != keyJson) throw new InvalidOperationException("business_key_hash_collision");
        record.Generation = generation;
        record.Name = SourceBusinessCatalog.Name(safe, table.Label);
        record.SourceStatus = SourceBusinessCatalog.Status(safe);
        record.SnapshotJson = JsonSerializer.Serialize(safe);
        record.SnapshotHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.SnapshotJson)));
        record.IsDeleted = SourceBusinessCatalog.Deleted(safe);
        record.UpdatedDate = DateTime.UtcNow;
        if (run != Guid.Empty) record.LastSeenRunId = run;
        if (eventId is not null) record.LastRawEventId = eventId;
    }

    private static string KeyHash(SourceSyncTable manifest, IReadOnlyDictionary<string, string?> values) =>
        Convert.ToHexString(SourceSyncCodec.Hash(manifest.PrimaryKey.Select(column => values[column])));
    private static void Bind(CoreBusinessRecord record, Guid installation)
    {
        if (record.InstallationId != installation) throw new InvalidOperationException("business_installation_changed");
    }
    private void DetachOwnRecords()
    {
        foreach (var entry in context.ChangeTracker.Entries<CoreBusinessRecord>().ToArray()) entry.State = EntityState.Detached;
    }
}
