using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CorePlasma.API.SourceSync;
using CorePlasma.API.SourceBusiness;
using CorePlasma.Common.Enums;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.API.SourceProjection;

/// <summary>Native Core records with exact, externally owned workflow. Never executes approval/stock commands.</summary>
public sealed class NativeBusinessProjector(ApplicationDbContext db, NativeProfileProjector identities)
{
    public JsonElement Snapshot(string source, string entity, SourceSyncRawRow row, SourceProjectionCatalog catalog, bool deleting)
    {
        var id = SourceSnapshotBuilder.Id(source, row.Values);
        Dictionary<string, object?> related = new(StringComparer.Ordinal);
        if (!deleting)
        {
            var relations = (source, entity) switch
            {
                ("quanly", "order") => new[] { ("order_items", "order_id"), ("order_tank_items", "order_id"), ("order_shippings", "order_id") },
                ("newapp", "order") => [("OrderDetail", "OrderId"), ("CustomerTank", "OrderId"), ("OrderShipping", "OrderId"), ("OrderWarehouse", "OrderId")],
                ("quanly", "transfer") => [("move_items", "move_tank_id"), ("move_tank_items", "move_tank_id"), ("move_tank_shippings", "move_tank_id")],
                ("newapp", "transfer") => [("MoveWarehouseOrderTank", "MoveWarehouseOrderId")],
                _ => Array.Empty<(string, string)>()
            };
            foreach (var (table, fk) in relations)
                related[table] = catalog.Related(table, fk, id).OrderBy(child => SourceSnapshotBuilder.Id(source, child.Values)).Select(child => SourceBusinessCatalog.Sanitize(table, child.Values)).ToArray();
            var n = source == "newapp";
            var actorTable = n ? "User" : "users";
            var actorFields = entity == "order" ? n ? new[] { "CreatedBy", "ResellerId", "ApproverId" } : ["order_by_id", "approver_id", "shipper_id"]
                : n ? ["CreatedBy"] : ["created_by_id", "approver_id", "completed_by_id", "verified_by_id", "cancelled_by_id"];
            if (catalog.ContainsTable(actorTable)) related["_actors"] = actorFields.Select(field => Optional(row.Values, field)).Where(value => value is > 0).Distinct().Order()
                .Select(value => catalog.Find(actorTable, value, n ? "Id" : "id")).Where(actor => actor is not null)
                .Select(actor => SourceBusinessCatalog.Sanitize(actorTable, actor!.Values)).ToArray();
            var locationTable = n ? "Warehouse" : "warehouses";
            var locationFields = entity == "transfer" ? n ? new[] { "FromId", "ToId" } : ["from_warehouse_id", "to_warehouse_id"] : n ? [] : new[] { "warehouse_id" };
            var locationIds = locationFields.Select(field => Optional(row.Values, field)).Where(value => value is > 0).ToList();
            if (n && entity == "order") locationIds.AddRange(catalog.Related("OrderWarehouse", "OrderId", id).Select(child => Optional(child.Values, "WarehouseId")).Where(value => value is > 0));
            if (catalog.ContainsTable(locationTable)) related["_locations"] = locationIds.Distinct().Order().Select(value => catalog.Find(locationTable, value, n ? "Id" : "id"))
                .Where(location => location is not null).Select(location => SourceBusinessCatalog.Sanitize(locationTable, location!.Values)).ToArray();
        }
        return JsonSerializer.SerializeToElement(new { row = SourceBusinessCatalog.Sanitize(row.Table, row.Values), related, deleted = deleting });
    }

    public async Task<NativeProjectionState> ApplyAsync(string source, Guid installation, string entity, SourceSyncRawRow row,
        SourceProjectionCatalog catalog, JsonElement snapshot, bool deleting, CancellationToken ct)
    {
        var id = SourceSnapshotBuilder.Id(source, row.Values);
        var hash = SourceProjectionIdentity.Hash(snapshot);
        deleting |= IsDeleted(source, row.Values);
        var bound = await db.Database.SqlQuery<Guid>($"""
            SELECT CoreId AS Value FROM source_sync.NativeBindings WHERE Source={source} AND InstallationId={installation} AND Entity={entity} AND EntityId={id}
            """).SingleOrDefaultAsync(ct);
        // Resolve source FKs BEFORE opening the native transaction, since an explicit alias bind has its own transaction.
        if (entity == "order") return await ApplyOrderAsync(source, installation, id, row.Values, catalog, hash, snapshot, bound, deleting, ct);
        if (entity == "transfer") return await ApplyTransferAsync(source, installation, id, row.Values, catalog, hash, snapshot, bound, deleting, ct);
        throw new SourceProjectionException("unsupported_business_entity");
    }

    private async Task<NativeProjectionState> ApplyOrderAsync(string source, Guid installation, long id, IReadOnlyDictionary<string, string?> row,
        SourceProjectionCatalog catalog, string hash, JsonElement snapshot, Guid bound, bool deleting, CancellationToken ct)
    {
        var n = source == "newapp";
        var customer = deleting ? null : await identities.ResolveAsync(source, "customer", SourceSnapshotBuilder.OptionalInteger(row, n ? "CustomerId" : "customer_id"), ct, allowDeleted: true);
        var creator = deleting ? null : await ResolveActorAsync(source, Optional(row, n ? "CreatedBy" : "order_by_id"), catalog, ct);
        var shipper = deleting || n ? null : await ResolveActorAsync(source, Optional(row, "shipper_id"), catalog, ct);
        var tanks = new List<Guid>();
        if (!deleting)
        {
            foreach (var child in catalog.Related(n ? "CustomerTank" : "order_tank_items", n ? "OrderId" : "order_id", id))
            {
                var tank = await identities.ResolveAsync(source, "tank", SourceSnapshotBuilder.OptionalInteger(child.Values, n ? "TankId" : "tank_id"), ct, allowDeleted: true);
                if (tank is not null) tanks.Add(tank.Value);
            }
        }
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            Order? order = bound != Guid.Empty ? await db.Orders.IgnoreQueryFilters().Include(x => x.Tanks).SingleOrDefaultAsync(x => x.Id == bound, ct) : null;
            if (bound != Guid.Empty && order is null) throw new SourceProjectionException("bound_native_order_missing");
            if (order is null && n && row.TryGetValue("OldId", out var oldId) && oldId is not null && Regex.IsMatch(oldId, "\\A[0-9a-fA-F]{24}\\z"))
            {
                // Only the explicit source OldId may bind a PLT ObjectId. Numeric IDs/numbers never do.
                order = await db.Orders.IgnoreQueryFilters().Include(x => x.Tanks).SingleOrDefaultAsync(x => x.LegacyId == oldId, ct);
            }
            if (order is null && deleting) { await tx.CommitAsync(ct); return new("order", id, null, hash, "applied", null); }
            if (order is null)
            {
                order = new Order { OrderNumber = id <= int.MaxValue ? (int)id : 0,
                    LegacyId = $"{source}:order:{id}", CreatedDate = DateTime.UtcNow };
                db.Orders.Add(order);
            }
            EnsureOwner(order.SourceSystem, order.SourceRecordId, source, id);
            order.SourceManaged = true;
            order.SourceSystem = SourceProjectionIdentity.SourceSystem(source);
            order.SourceRecordId = id.ToString(CultureInfo.InvariantCulture);
            order.SourceDetailsJson = snapshot.GetRawText();
            order.Status = OrderStatus.ExternalWorkflow;
            order.IsDeleted = deleting;
            if (!deleting)
            {
                var status = SourceSnapshotBuilder.Text(row, n ? "Status" : "status") ?? throw new SourceProjectionException("order_status_missing");
                order.SourceStatusCode = status;
                order.SourceStatusLabel = OrderStatusLabel(source, status);
                order.OrderDate = Date(row, n ? "CreatedDate" : "created_at");
                order.SourceUpdatedAt = OptionalDate(row, n ? "UpdatedDate" : "updated_at");
                order.CustomerId = customer;
                order.CreatedByEmployeeId = creator;
                order.DeliveryEmployeeId = shipper;
                order.Note = SourceSnapshotBuilder.Text(SourceBusinessCatalog.Sanitize(n ? "Order" : "orders", row), n ? "Note" : "order_note");
                order.OrderType = OrderType.ExternalWorkflow;
                // Exact source type ('test' included) and requested quantities remain in SourceDetailsJson.
                foreach (var item in order.Tanks) item.IsDeleted = true;
                var sort = 0;
                foreach (var tank in tanks.Distinct())
                {
                    var item = order.Tanks.FirstOrDefault(x => x.TankId == tank);
                    if (item is null) { item = new OrderTank { OrderId = order.Id, TankId = tank, CreatedDate = DateTime.UtcNow }; order.Tanks.Add(item); }
                    item.IsDeleted = false; item.SortOrder = sort++;
                }
            }
            else foreach (var item in order.Tanks) item.IsDeleted = true;
            await db.SaveChangesAsync(ct);
            await BindAsync(source, installation, "order", id, order.Id, ct);
            await tx.CommitAsync(ct);
            return new("order", id, order.Id, hash, "applied", null);
        }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }

    private async Task<NativeProjectionState> ApplyTransferAsync(string source, Guid installation, long id, IReadOnlyDictionary<string, string?> row,
        SourceProjectionCatalog catalog, string hash, JsonElement snapshot, Guid bound, bool deleting, CancellationToken ct)
    {
        var n = source == "newapp";
        var tanks = new List<(Guid Id, string Code)>();
        if (!deleting)
        {
            foreach (var child in catalog.Related(n ? "MoveWarehouseOrderTank" : "move_tank_items", n ? "MoveWarehouseOrderId" : "move_tank_id", id))
            {
                var tankId = await identities.ResolveAsync(source, "tank", SourceSnapshotBuilder.OptionalInteger(child.Values, n ? "TankId" : "tank_id"), ct, allowDeleted: true);
                if (tankId is null) throw new SourceProjectionException("transfer_tank_identity_required");
                var code = await db.Tanks.IgnoreQueryFilters().Where(x => x.Id == tankId).Select(x => x.TankCode).SingleAsync(ct);
                tanks.Add((tankId.Value, code));
            }
        }
        var toWarehouse = deleting ? null : catalog.Find(n ? "Warehouse" : "warehouses", SourceSnapshotBuilder.OptionalInteger(row, n ? "ToId" : "to_warehouse_id"), n ? "Id" : "id");
        if (!deleting && toWarehouse is null) throw new SourceProjectionException("transfer_destination_missing");
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var transfer = bound == Guid.Empty ? null : await db.TankTransfers.IgnoreQueryFilters().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == bound, ct);
            if (bound != Guid.Empty && transfer is null) throw new SourceProjectionException("bound_native_transfer_missing");
            if (transfer is null && deleting) { await tx.CommitAsync(ct); return new("transfer", id, null, hash, "applied", null); }
            if (transfer is null)
            {
                transfer = new TankTransfer { VoucherNo = $"{(n ? "NEWAPP" : "QUANLY")}-MOVE-{id}", CreatedDate = DateTime.UtcNow };
                db.TankTransfers.Add(transfer);
            }
            EnsureOwner(transfer.SourceSystem, transfer.SourceRecordId, source, id);
            transfer.SourceManaged = true; transfer.SourceSystem = SourceProjectionIdentity.SourceSystem(source);
            transfer.SourceRecordId = id.ToString(CultureInfo.InvariantCulture); transfer.SourceDetailsJson = snapshot.GetRawText();
            transfer.Status = VoucherStatus.ExternalWorkflow; transfer.IsDeleted = deleting;
            if (!deleting)
            {
                // NEWAPP records warehouse movements without a status column. Keep that distinction explicit.
                transfer.SourceStatusCode = n ? "recorded" : SourceSnapshotBuilder.Text(row, "status");
                transfer.SourceStatusLabel = n ? "Đã ghi nhận điều chuyển" : TransferStatusLabel(transfer.SourceStatusCode);
                transfer.VoucherDate = Date(row, n ? "CreatedDate" : "created_at");
                transfer.ToLocation = SourceSnapshotBuilder.Text(toWarehouse!.Values, n ? "Name" : "name");
                transfer.Note = n ? null : SourceSnapshotBuilder.Text(SourceBusinessCatalog.Sanitize("move_tanks", row), "note");
                // Reconcile only this source-owned voucher's items; never change Tanks/stock as a side effect.
                // Retaining existing item IDs also avoids an insert/delete collision on the unique pair.
                var desired = tanks.DistinctBy(x => x.Id).ToDictionary(tank => tank.Id);
                foreach (var removed in transfer.Items.Where(item => !desired.ContainsKey(item.TankId)).ToArray())
                { db.TankTransferItems.Remove(removed); transfer.Items.Remove(removed); }
                foreach (var tank in desired.Values)
                {
                    var item = transfer.Items.SingleOrDefault(item => item.TankId == tank.Id);
                    if (item is null) transfer.Items.Add(new TankTransferItem { TankTransferId = transfer.Id, TankId = tank.Id, TankCode = tank.Code });
                    else item.TankCode = tank.Code;
                }
            }
            await db.SaveChangesAsync(ct);
            await BindAsync(source, installation, "transfer", id, transfer.Id, ct);
            await tx.CommitAsync(ct);
            return new("transfer", id, transfer.Id, hash, "applied", null);
        }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }

    private Task BindAsync(string source, Guid installation, string entity, long id, Guid core, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            IF NOT EXISTS (SELECT 1 FROM source_sync.NativeBindings WHERE Source={source} AND InstallationId={installation} AND Entity={entity} AND EntityId={id})
            INSERT source_sync.NativeBindings(Source,InstallationId,Entity,EntityId,CoreId) VALUES({source},{installation},{entity},{id},{core});
            """, ct);

    private async Task<Guid?> ResolveActorAsync(string source, long? id, SourceProjectionCatalog catalog, CancellationToken ct)
    {
        if (id is null or 0) return null;
        var actor = catalog.Find(source == "newapp" ? "User" : "users", id, source == "newapp" ? "Id" : "id")
            ?? throw new SourceProjectionException("source_actor_missing");
        // A customer/device login is a descriptive source actor, never a Core Employee/User import.
        if (!SourceSnapshotBuilder.IsStaff(source, actor.Values)) return null;
        return await identities.ResolveAsync(source, "user", id, ct, allowDeleted: true);
    }

    private static void EnsureOwner(string? owner, string? recordId, string source, long id)
    {
        if (owner is not null && (owner != SourceProjectionIdentity.SourceSystem(source) || recordId != id.ToString(CultureInfo.InvariantCulture)))
            throw new SourceProjectionException("canonical_business_record_owned_by_another_source");
    }

    public static bool IsDeleted(string source, IReadOnlyDictionary<string, string?> row) => source == "newapp"
        ? row.TryGetValue("IsDeleted", out var deleted) && deleted is "true" or "t"
        : row.TryGetValue("deleted_at", out var legacyDeleted) && legacyDeleted is not null;
    private static long? Optional(IReadOnlyDictionary<string, string?> row, string field) => row.ContainsKey(field) ? SourceSnapshotBuilder.OptionalInteger(row, field) : null;
    private static DateTime? OptionalDate(IReadOnlyDictionary<string, string?> row, string field) => row.TryGetValue(field, out var text) && text is not null ? ParseDate(text) : null;
    private static DateTime Date(IReadOnlyDictionary<string, string?> row, string field) => OptionalDate(row, field) ?? throw new SourceProjectionException("source_business_date_missing");
    public static DateTime ParseDate(string value) => DateTime.TryParse(value, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : throw new SourceProjectionException("invalid_source_timestamp");

    public static string OrderStatusLabel(string source, string status) => source == "newapp" ? status switch
    {
        "0" => "Mới", "1" => "Đã xác nhận phân kho", "2" => "Đã hủy", "3" => "Đang giao", "4" => "Đã đóng", "5" => "Đã duyệt", _ => status
    } : status switch
    {
        "new" => "Mới", "adjusting" => "Đang điều chỉnh", "confirmed" => "Đã xác nhận", "ready_to_ship" => "Sẵn sàng giao",
        "shipping" => "Đang giao", "delivery_failed" => "Giao thất bại", "delivered" => "Đã giao", "verify_failed" => "Xác minh thất bại",
        "completed" => "Hoàn tất", "cancelled" => "Đã hủy", _ => status
    };
    private static string? TransferStatusLabel(string? status) => status switch
    {
        "new" => "Mới", "adjusting" => "Đang điều chỉnh", "confirmed" => "Đã xác nhận", "shipping" => "Đang vận chuyển",
        "delivered" => "Đã giao", "verify" => "Đang xác minh", "completed" => "Hoàn tất", "cancelled" => "Đã hủy", _ => status
    };
}
