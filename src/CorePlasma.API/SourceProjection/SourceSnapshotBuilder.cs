using System.Globalization;
using System.Text.Json;
using CorePlasma.API.SourceSync;

namespace CorePlasma.API.SourceProjection;

/// <summary>Explicit adapters matching the existing source-native SQL snapshots. Raw storage is never normalized.</summary>
public static class SourceSnapshotBuilder
{
    // Checkpoint-only version: a source-contract fix reconsiders blocked rows
    // without changing unchanged applied/protected per-record fingerprints.
    public const string AdapterVersion = "v2-source-schema";
    private static readonly HashSet<string> StaffRoles = ["sysadmin", "manager", "reseller", "warehouse_keeper",
        "cs_staff", "cs_lead", "accountant", "factory_manager", "shipper"];

    public static string[] Tables(string source) => source switch
    {
        "quanly" => ["customers", "users", "engines", "tanks", "capacities", "warehouses", "factories", "orders", "order_items", "order_tank_items", "order_shippings", "move_tanks", "move_items", "move_tank_items", "move_tank_shippings"],
        "newapp" => ["Customer", "User", "Tank", "Capacity", "Warehouse", "CustomerTank", "WarehouseTank", "TankCycle", "Order", "OrderDetail", "OrderShipping", "OrderWarehouse", "TankEngineHistory", "MoveWarehouseOrder", "MoveWarehouseOrderTank"],
        _ => throw new SourceProjectionException("unknown_source")
    };

    public static string? Entity(string source, string table) => (source, table) switch
    {
        ("quanly", "customers") or ("newapp", "Customer") => "customer",
        ("quanly", "users") or ("newapp", "User") => "user",
        ("quanly", "tanks") or ("newapp", "Tank") => "tank",
        ("quanly", "engines") => "engine",
        ("quanly", "orders") or ("newapp", "Order") => "order",
        ("newapp", "TankEngineHistory") => "tank-engine-history",
        ("quanly", "move_tanks") or ("newapp", "MoveWarehouseOrder") => "transfer",
        _ => null
    };

    public static bool IsDirect(string source, string table) => source == "newapp" && table is "Engine" or "Session" or "TankSession";
    public static bool IsSupport(string source, string table) => (source, table) switch
    {
        ("quanly", "capacities" or "warehouses" or "factories" or "order_items" or "order_tank_items" or "order_shippings" or "move_items" or "move_tank_items" or "move_tank_shippings") => true,
        ("newapp", "Capacity" or "Warehouse" or "CustomerTank" or "WarehouseTank" or "TankCycle" or "OrderDetail" or "OrderShipping" or "OrderWarehouse" or "MoveWarehouseOrderTank") => true,
        _ => false
    };

    public static long Id(string source, IReadOnlyDictionary<string, string?> row) => Integer(row, source == "quanly" ? "id" : "Id");

    public static JsonElement Build(string source, string entity, SourceSyncRawRow row, SourceProjectionCatalog catalog, bool deleting = false)
    {
        var r = row.Values;
        var id = Id(source, r);
        if (!deleting) deleting = source == "newapp" ? Boolean(r, "IsDeleted") : r.TryGetValue("deleted_at", out var deleted) && deleted is not null;
        if (deleting) return JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["id"] = id, ["deleted_at"] = "source-delete" });
        var n = source == "newapp";
        Dictionary<string, object?> result = new() { ["id"] = id };
        switch (entity)
        {
            case "customer":
                result["name"] = Text(r, n ? "Name" : "name");
                result["address"] = Text(r, n ? "Address" : "address");
                result["phone_number"] = Text(r, n ? "Tel" : "phone_number");
                result["status"] = n ? (Boolean(r, "IsDeleted") ? "inactive" : "active") : Text(r, "status");
                break;
            case "user":
                var excluded = !IsStaff(source, r);
                result["name"] = Text(r, n ? "Name" : "name");
                result["email"] = Text(r, n ? "Email" : "email");
                result["phone_number"] = Text(r, n ? "PhoneNumber" : "phone_number");
                result["is_active"] = Boolean(r, n ? "IsActive" : "is_active");
                // Excluded customer/device users can tombstone a former verified staff profile, never create one.
                if (n) result["user_type"] = Integer(r, "UserType");
                if (excluded) result["deleted_at"] = "not-staff-profile";
                break;
            case "engine":
                if (n) throw new SourceProjectionException("engine_owned_by_direct_stream");
                result["name"] = Text(r, "name");
                result["serial"] = Text(r, "serial");
                result["old_id"] = Text(r, "old_id");
                result["status"] = Boolean(r, "is_lock") ? "locked" : Text(r, "status") switch
                {
                    "ready" or "at_client" => "active", "fixing" => "repair", var status => status
                };
                break;
            case "tank":
                result["_source_status"] = n ? Integer(r, "Status") : Text(r, "status");
                result["serial"] = Text(r, n ? "Serial" : "serial");
                result["old_id"] = Text(r, n ? "OldId" : "old_id");
                result["status"] = n ? Integer(r, "Status") switch
                {
                    0 => "ready", 1 => "shipping", 2 => "attached", 3 => "detached", 4 => "to_be_refill",
                    5 => "broken", 6 => "occupied", 7 => "empty", _ => throw new SourceProjectionException("unknown_tank_status")
                } : Text(r, "status");
                result["customer_id"] = n ? CurrentAssociation(catalog, "CustomerTank", id, "CustomerId", "ReturnedTime") : OptionalInteger(r, "customer_id");
                var capacity = catalog.Find(n ? "Capacity" : "capacities", OptionalInteger(r, n ? "CapacityId" : "capacity_id"), n ? "Id" : "id");
                if (capacity is null) throw new SourceProjectionException("capacity_mapping_missing");
                result["_capacity"] = new Dictionary<string, object?> { ["id"] = Id(source, capacity.Values),
                    ["name"] = Text(capacity.Values, n ? "Name" : "name"), ["value"] = Decimal(capacity.Values, n ? "Value" : "value") };
                var warehouseId = n ? CurrentAssociation(catalog, "WarehouseTank", id, "WarehouseId", "MoveOutTime") : OptionalInteger(r, "warehouse_id");
                result["_warehouse"] = Lookup(source, catalog, n ? "Warehouse" : "warehouses", warehouseId);
                result["_factory"] = n ? null : Lookup(source, catalog, "factories", OptionalInteger(r, "factory_id"));
                break;
            default: throw new SourceProjectionException("unsupported_entity");
        }
        if (!result.ContainsKey("deleted_at")) result["deleted_at"] = n ? Boolean(r, "IsDeleted") ? "source-soft-delete" : null
            // Quanly tanks/engines have physical deletion only in the verified
            // source schema. Customer/staff soft-delete fields stay required.
            : entity is "tank" or "engine" ? r.GetValueOrDefault("deleted_at") : Text(r, "deleted_at");
        return JsonSerializer.SerializeToElement(result);
    }

    private static long? CurrentAssociation(SourceProjectionCatalog catalog, string table, long tank, string reference, string ended)
    {
        var ids = new HashSet<long>();
        foreach (var relation in catalog.Related(table, "TankId", tank))
        {
            var r = relation.Values;
            if (Integer(r, "TankId") != tank || Text(r, ended) is not null) continue;
            var cycle = catalog.Find("TankCycle", OptionalInteger(r, "TankCycleId"), "Id");
            if (cycle is null) throw new SourceProjectionException("tank_cycle_missing");
            if (Boolean(cycle.Values, "IsDeleted") || Text(cycle.Values, "EndedTime") is not null) continue;
            var id = Integer(r, reference);
            if (id > 0) ids.Add(id);
        }
        if (ids.Count > 1) throw new SourceProjectionException("ambiguous_tank_assignment");
        return ids.Count == 0 ? null : ids.Single();
    }

    private static object? Lookup(string source, SourceProjectionCatalog catalog, string table, long? id)
    {
        var row = catalog.Find(table, id, source == "quanly" ? "id" : "Id");
        if (id is > 0 && row is null) throw new SourceProjectionException("location_lookup_missing");
        if (row is null || source == "newapp" && Boolean(row.Values, "IsDeleted")) return null;
        return new Dictionary<string, object?> { ["id"] = id, ["name"] = Text(row.Values, source == "quanly" ? "name" : "Name") };
    }

    public static string? Text(IReadOnlyDictionary<string, string?> row, string field) => row.TryGetValue(field, out var value)
        ? value : throw new SourceProjectionException("missing_column:" + field);
    public static bool IsStaff(string source, IReadOnlyDictionary<string, string?> row) => source == "newapp"
        ? Integer(row, "UserType") is not (3 or 6 or 10) : StaffRoles.Contains(Text(row, "user_role") ?? "");
    public static long Integer(IReadOnlyDictionary<string, string?> row, string field) => OptionalInteger(row, field)
        ?? throw new SourceProjectionException("null_integer:" + field);
    public static long? OptionalInteger(IReadOnlyDictionary<string, string?> row, string field)
    {
        var value = Text(row, field);
        if (value is null) return null;
        return long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            ? number : throw new SourceProjectionException("invalid_integer:" + field);
    }
    public static bool Boolean(IReadOnlyDictionary<string, string?> row, string field) => Text(row, field) switch
    {
        "true" or "t" => true, "false" or "f" => false, _ => throw new SourceProjectionException("invalid_boolean:" + field)
    };
    public static decimal Decimal(IReadOnlyDictionary<string, string?> row, string field) =>
        decimal.TryParse(Text(row, field), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value : throw new SourceProjectionException("invalid_decimal:" + field);
}
