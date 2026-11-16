using System.Text.Json;
using CorePlasma.API.SourceSync;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.API.SourceProjection;

public sealed class NativeTankEngineHistoryProjector(ApplicationDbContext db, NativeProfileProjector identities)
{
    public async Task<NativeProjectionState> ApplyAsync(SourceSyncRawRow row, SourceProjectionCatalog catalog, bool deleting, CancellationToken ct)
    {
        var r = row.Values;
        var id = SourceSnapshotBuilder.Id("newapp", r);
        var hash = SourceProjectionIdentity.Hash(JsonSerializer.SerializeToElement(new { row = r, deleted = deleting }));
        var engine = deleting ? null : await identities.ResolveAsync("newapp", "engine", SourceSnapshotBuilder.Integer(r, "EngineId"), ct, allowDeleted: true);
        Guid? tank = null;
        var rfid = r.TryGetValue("RFID", out var tag) ? tag : deleting ? null : throw new SourceProjectionException("history_rfid_field_missing");
        if (!deleting && rfid is not null)
        {
            var tankRows = catalog.RelatedText("Tank", "RFID", rfid).Take(2).ToArray();
            if (tankRows.Length == 1) tank = await identities.ResolveAsync("newapp", "tank", SourceSnapshotBuilder.Id("newapp", tankRows[0].Values), ct, allowDeleted: true);
        }
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // This LegacyId is explicitly documented as NEWAPP TankEngineHistory.Id, unlike PLT order IDs.
            var target = await db.TankEngineHistories.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.LegacyId == id, ct);
            if (target is null && deleting) { await tx.CommitAsync(ct); return new("tank-engine-history", id, null, hash, "applied", null); }
            if (target is null) { target = new TankEngineHistory { LegacyId = id, CreatedDate = DateTime.UtcNow }; db.TankEngineHistories.Add(target); }
            target.IsDeleted = deleting;
            if (!deleting)
            {
                var type = SourceSnapshotBuilder.Integer(r, "Type");
                if (type is not (0 or 1)) throw new SourceProjectionException("unknown_tank_engine_history_type");
                target.EngineId = engine ?? throw new SourceProjectionException("history_engine_identity_required");
                target.TankId = tank; target.TankRfid = rfid; target.EventType = (int)type;
                target.EventTime = NativeBusinessProjector.ParseDate(SourceSnapshotBuilder.Text(r, "CreateTime") ?? throw new SourceProjectionException("history_time_missing"));
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new("tank-engine-history", id, target.Id, hash, "applied", null);
        }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }
}
