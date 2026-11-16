using System.Text.Json;
using System.Text.RegularExpressions;
using CorePlasma.BAL.DTOs.Integration;
using CorePlasma.BAL.Interfaces;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.API.SourceProjection;

public sealed class NativeProfileProjector(ApplicationDbContext db, ILegacyEventIngestService legacy)
{
    public async Task<NativeProjectionState> ApplyAsync(string source, Guid installation, Guid generation, string revision,
        string entity, long id, JsonElement snapshot, string operation, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var ns = SourceProjectionIdentity.Namespace(source);
        var mapping = await db.LegacySyncMappings.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Source == ns && x.Entity == entity && x.EntityId == id, ct);
        if (mapping?.CoreId is null)
        {
            // Representative IDs are verified aliases. Serial/name/phone are never identity authority.
            var verified = await FindRepresentativeAsync(source, entity, id, ct);
            var explicitLegacy = await FindExplicitLegacyIdAsync(entity, snapshot, ct);
            if (verified is not null && explicitLegacy is not null && verified != explicitLegacy)
                throw new SourceProjectionException("conflicting_verified_source_identities");
            verified ??= explicitLegacy;
            if (verified is not null)
            {
                await legacy.BindMappingAsync(new LegacyMappingBindDto { Source = ns, SourceSystem = SourceProjectionIdentity.SourceSystem(source),
                    Entity = entity, EntityId = id, CoreId = verified.Value }, ct);
                db.ChangeTracker.Clear();
            }
            else if (operation is not ("create" or "delete"))
                throw new SourceProjectionException("verified_source_identity_required");
        }
        var hash = SourceProjectionIdentity.Hash(snapshot);
        var result = await legacy.IngestProjectionAsync(new LegacyEventIngestDto
        {
            EventId = SourceProjectionIdentity.EventId(source, installation, generation, revision, entity, id, hash),
            Source = ns, SourceSystem = SourceProjectionIdentity.SourceSystem(source), Entity = entity, EntityId = id,
            Operation = operation, OccurredAt = occurredAt, Data = snapshot
        }, ct);
        var coreId = await db.LegacySyncMappings.AsNoTracking().Where(x => x.Source == ns && x.Entity == entity && x.EntityId == id)
            .Select(x => x.CoreId).SingleAsync(ct);
        return new NativeProjectionState(entity, id, coreId, hash, result.Status == "protected" ? "protected" : "applied",
            result.Status == "protected" ? "canonical_profile_protected" : null);
    }

    public async Task<Guid?> ResolveAsync(string source, string entity, long? id, CancellationToken ct, bool allowDeleted = false)
    {
        if (id is null or 0) return null;
        var ns = SourceProjectionIdentity.Namespace(source);
        var mapped = await db.LegacySyncMappings.AsNoTracking().Where(x => x.Source == ns && x.Entity == entity && x.EntityId == id && (allowDeleted || !x.IsDeleted))
            .Select(x => x.CoreId).SingleOrDefaultAsync(ct);
        if (mapped is not null)
        {
            var exists = entity switch
            {
                "customer" => await db.Customers.IgnoreQueryFilters().AnyAsync(x => x.Id == mapped && (allowDeleted || !x.IsDeleted), ct),
                "tank" => await db.Tanks.IgnoreQueryFilters().AnyAsync(x => x.Id == mapped && (allowDeleted || !x.IsDeleted), ct),
                "engine" => await db.Engines.IgnoreQueryFilters().AnyAsync(x => x.Id == mapped && (allowDeleted || !x.IsDeleted), ct),
                "user" => await db.Employees.IgnoreQueryFilters().AnyAsync(x => x.Id == mapped && (allowDeleted || !x.IsDeleted), ct), _ => false
            };
            if (exists) return mapped;
            throw new SourceProjectionException("verified_native_reference_deleted_or_missing");
        }
        var representative = await FindRepresentativeAsync(source, entity, id.Value, ct);
        if (representative is null) throw new SourceProjectionException("verified_native_reference_required");
        if (allowDeleted) return representative;
        await legacy.BindMappingAsync(new LegacyMappingBindDto { Source = ns, SourceSystem = SourceProjectionIdentity.SourceSystem(source),
            Entity = entity, EntityId = id.Value, CoreId = representative.Value }, ct);
        db.ChangeTracker.Clear();
        return representative;
    }

    private async Task<Guid?> FindRepresentativeAsync(string source, string entity, long id, CancellationToken ct)
    {
        var q = source == "quanly";
        List<Guid> ids = entity switch
        {
            "customer" => await db.Customers.IgnoreQueryFilters().Where(x => q ? x.QuanLyId == id : x.NewAppId == id).Select(x => x.Id).Take(2).ToListAsync(ct),
            "tank" => await db.Tanks.IgnoreQueryFilters().Where(x => q ? x.QuanLyId == id : x.NewAppId == id).Select(x => x.Id).Take(2).ToListAsync(ct),
            "engine" => await db.Engines.IgnoreQueryFilters().Where(x => q ? x.QuanLyId == id : x.NewAppId == id).Select(x => x.Id).Take(2).ToListAsync(ct),
            "user" => await db.Employees.IgnoreQueryFilters().Where(x => q ? x.QuanLyId == id : x.NewAppId == id).Select(x => x.Id).Take(2).ToListAsync(ct),
            _ => throw new SourceProjectionException("unsupported_reference_entity")
        };
        if (ids.Count > 1) throw new SourceProjectionException("ambiguous_verified_source_identity");
        return ids.Count == 1 ? ids[0] : null;
    }

    private async Task<Guid?> FindExplicitLegacyIdAsync(string entity, JsonElement snapshot, CancellationToken ct)
    {
        if (entity is not ("tank" or "engine") || !snapshot.TryGetProperty("old_id", out var original) ||
            original.ValueKind != JsonValueKind.String || original.GetString() is not { } legacy) return null;
        string[] aliases;
        if (Regex.IsMatch(legacy, "\\A[0-9a-fA-F]{24}\\z", RegexOptions.CultureInvariant))
            aliases = [legacy.ToLowerInvariant(), legacy.ToUpperInvariant()];
        else if (Guid.TryParseExact(legacy, "D", out var guid) || Guid.TryParseExact(legacy, "N", out guid))
            aliases = [guid.ToString("D"), guid.ToString("D").ToUpperInvariant(), guid.ToString("N"), guid.ToString("N").ToUpperInvariant()];
        else return null; // Numeric IDs, source prefixes and physical identifiers cannot establish a cross-system identity.
        var ids = entity == "tank"
            ? await db.Tanks.IgnoreQueryFilters().Where(tank => tank.LegacyId != null && aliases.Contains(tank.LegacyId)).Select(tank => tank.Id).Take(2).ToListAsync(ct)
            : await db.Engines.IgnoreQueryFilters().Where(engine => engine.LegacyId != null && aliases.Contains(engine.LegacyId)).Select(engine => engine.Id).Take(2).ToListAsync(ct);
        if (ids.Count > 1) throw new SourceProjectionException("ambiguous_explicit_legacy_identity");
        return ids.Count == 1 ? ids[0] : null;
    }
}
