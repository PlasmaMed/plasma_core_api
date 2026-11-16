using CorePlasma.DAL.Context;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.API.SourceProjection;

public sealed class NativeProjectionStore(ApplicationDbContext db)
{
    public async Task<long[]> ReadOwnedEntityIdsAsync(string source, Guid installation, Guid generation, string entity, CancellationToken ct)
    {
        // Native mutations and projection metadata have separate transactions.
        // Ownership ledgers commit with native data, so TRUNCATE must also find
        // a committed row whose metadata save was interrupted by a crash.
        var known = db.Database.SqlQuery<long>($"""
            SELECT EntityId AS Value FROM source_sync.NativeProjection
            WHERE Source={source} AND InstallationId={installation} AND Generation={generation}
              AND Entity={entity} AND EntityId>0 AND CoreId IS NOT NULL
            """);
        var ids = await known.ToListAsync(ct);
        if (entity is "customer" or "user" or "tank" or "engine")
        {
            if (source == "newapp" && entity == "engine") throw new SourceProjectionException("engine_owned_by_direct_stream");
            var ns = SourceProjectionIdentity.Namespace(source);
            var system = SourceProjectionIdentity.SourceSystem(source);
            ids.AddRange(await db.LegacySyncMappings.AsNoTracking().Where(mapping => mapping.Source == ns && mapping.SourceSystem == system
                && mapping.Entity == entity && mapping.CoreId != null && mapping.EntityId > 0).Select(mapping => mapping.EntityId).ToListAsync(ct));
        }
        else if (entity is "order" or "transfer")
        {
            ids.AddRange(await db.Database.SqlQuery<long>($"""
                SELECT EntityId AS Value FROM source_sync.NativeBindings
                WHERE Source={source} AND InstallationId={installation} AND Entity={entity} AND EntityId>0
                """).ToListAsync(ct));
        }
        else if (entity == "tank-engine-history" && source == "newapp")
        {
            // This column is exclusively NEWAPP TankEngineHistory.Id. Core-only
            // history has LegacyId NULL and is never adopted by a raw TRUNCATE.
            ids.AddRange(await db.TankEngineHistories.IgnoreQueryFilters().Where(history => history.LegacyId != null && history.LegacyId > 0)
                .Select(history => history.LegacyId!.Value).ToListAsync(ct));
        }
        else throw new SourceProjectionException("unsupported_owned_projection_entity");
        return ids.Distinct().Order().ToArray();
    }

    public Task<NativeProjectionCheckpoint?> CheckpointAsync(string source, Guid installation, Guid generation, string entity, CancellationToken ct) =>
        db.Database.SqlQuery<NativeProjectionCheckpoint>($"""
            SELECT SnapshotHash, State, ErrorCode, Revision FROM source_sync.NativeProjection
            WHERE Source={source} AND InstallationId={installation} AND Generation={generation} AND Entity={entity} AND EntityId=1
            """).SingleOrDefaultAsync(ct);

    public async Task<bool> HasUnresolvedAsync(string source, Guid installation, Guid generation, string entity, CancellationToken ct) =>
        await db.Database.SqlQuery<int>($"""
            SELECT TOP(1) 1 AS Value FROM source_sync.NativeProjection
            WHERE Source={source} AND InstallationId={installation} AND Generation={generation} AND Entity={entity} AND State<>'applied'
            """).AnyAsync(ct);

    public Task<NativeProjectionState?> FindAsync(string source, Guid installation, Guid generation, string entity, long id, CancellationToken ct) =>
        db.Database.SqlQuery<NativeProjectionState>($"""
            SELECT Entity, EntityId, CoreId, SnapshotHash, State, ErrorCode
            FROM source_sync.NativeProjection
            WHERE Source={source} AND InstallationId={installation} AND Generation={generation} AND Entity={entity} AND EntityId={id}
            """).SingleOrDefaultAsync(ct);

    public Task<List<NativeProjectionState>> ReadProfilesAsync(string source, Guid installation, Guid generation, CancellationToken ct) =>
        db.Database.SqlQuery<NativeProjectionState>($"""
            SELECT Entity, EntityId, CoreId, SnapshotHash, State, ErrorCode
            FROM source_sync.NativeProjection
            WHERE Source={source} AND InstallationId={installation} AND Generation={generation} AND Entity <> 'tank-engine-history'
            """).ToListAsync(ct);

    public Task<List<NativeProjectionState>> ReadEntityAsync(string source, Guid installation, Guid generation, string entity, CancellationToken ct) =>
        db.Database.SqlQuery<NativeProjectionState>($"""
            SELECT Entity, EntityId, CoreId, SnapshotHash, State, ErrorCode
            FROM source_sync.NativeProjection WHERE Source={source} AND InstallationId={installation} AND Generation={generation} AND Entity={entity}
            """).ToListAsync(ct);

    public Task SaveAsync(string source, Guid installation, Guid generation, NativeProjectionState entry, string revision, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE source_sync.NativeProjection SET CoreId={entry.CoreId}, SnapshotHash={entry.SnapshotHash},
              State={entry.State}, ErrorCode={entry.ErrorCode}, Revision={revision}, UpdatedAt=SYSUTCDATETIME()
            WHERE Source={source} AND InstallationId={installation} AND Generation={generation} AND Entity={entry.Entity} AND EntityId={entry.EntityId};
            IF @@ROWCOUNT=0 INSERT source_sync.NativeProjection
              (Source,InstallationId,Generation,Entity,EntityId,CoreId,SnapshotHash,State,ErrorCode,Revision,UpdatedAt)
              VALUES({source},{installation},{generation},{entry.Entity},{entry.EntityId},{entry.CoreId},{entry.SnapshotHash},
                {entry.State},{entry.ErrorCode},{revision},SYSUTCDATETIME());
            """, ct);

    public static string ErrorCode(Exception exception) => exception switch
    {
        SourceProjectionException known => Clean(known.Code),
        CorePlasma.Common.Exceptions.ConflictException => "native_identity_or_ownership_conflict",
        CorePlasma.Common.Exceptions.BusinessException => "native_validation_conflict",
        _ => "native_projection_failed"
    };

    private static string Clean(string value) => new(value.Take(80).Select(character =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' ? character : '_').ToArray());
}
