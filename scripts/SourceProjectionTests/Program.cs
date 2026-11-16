using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CorePlasma.API.SourceProjection;
using CorePlasma.API.SourceBusiness;
using CorePlasma.API.SourceSync;
using CorePlasma.BAL.DTOs.Integration;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Models;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

if (Environment.GetEnvironmentVariable("SOURCE_PROJECTION_EMIT_SCHEMA") == "1")
{
    await using var schema = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlServer("Server=127.0.0.1;Database=core_schema_emit_only;User ID=not_used;Password=not_used;Encrypt=False").Options);
    Console.Write(schema.Database.GenerateCreateScript());
    return; // No connection is opened. No database/row mutation occurs in schema mode.
}
var checks = 0;
void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks++; Console.WriteLine("PASS " + label); }
SourceSyncRawRow Row(string table, params (string Key, string? Value)[] values) => new(table, values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
SourceProjectionCatalog Catalog(params SourceSyncRawRow[] rows) => new(rows.GroupBy(row => row.Table).ToDictionary(group => group.Key,
    group => (IReadOnlyList<SourceSyncRawRow>)group.ToArray(), StringComparer.Ordinal));
SourceProjectionCatalog EmptyCatalog() => new(new Dictionary<string, IReadOnlyList<SourceSyncRawRow>>());

SourceProjectionOptions BoundProjectionOptions(params string[] sources)
{
    var values = sources.Select((source, index) => new KeyValuePair<string, string?>("SourceProjection:Sources:" + index, source));
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    var registrations = new ServiceCollection();
    registrations.AddSourceProjectionServices(configuration);
    using var provider = registrations.BuildServiceProvider();
    return provider.GetRequiredService<IOptionsMonitor<SourceProjectionOptions>>().CurrentValue;
}
Check(BoundProjectionOptions("quanly", "newapp").Sources.SequenceEqual(new[] { "quanly", "newapp" }),
    "Real Options binding retains exactly two configured native sources without appending defaults");
Check(BoundProjectionOptions("quanly").Sources.SequenceEqual(new[] { "quanly" }),
    "A one-source native configuration never implicitly enables the other source");
Check(BoundProjectionOptions().Sources.Length == 0,
    "Missing native sources stays empty for the runner's fail-closed configuration validation");
Check(BoundProjectionOptions("quanly", "quanly").Sources.Length == 2,
    "Binding does not hide explicit duplicates from strict runner validation");

var customer = Row("customers", ("id", "7"), ("name", "Clinic"), ("address", null), ("phone_number", "+84 900000000"), ("status", "active"), ("deleted_at", null));
var customerSnapshot = SourceSnapshotBuilder.Build("quanly", "customer", customer, EmptyCatalog());
Check(customerSnapshot.GetProperty("id").GetInt64() == 7 && customerSnapshot.GetProperty("address").ValueKind == JsonValueKind.Null, "Customer IDs are numbers; source null stays null");
var quanlyTank = Row("tanks", ("id", "21"), ("rfid", "Q-RFID"), ("serial", "Q21"), ("old_id", null), ("status", "ready"),
    ("net_weight", "10"), ("capacity_id", "1"), ("created_at", "2026-11-09T00:00:00Z"), ("updated_at", null),
    ("warehouse_id", null), ("customer_id", null), ("order_id", null), ("last_sale_date", null), ("export_tank_id", null),
    ("factory_id", null), ("import_tank_id", null), ("revoke_tank_id", null), ("move_tank_id", null));
var quanlyCapacity = Row("capacities", ("id", "1"), ("name", "10L"), ("value", "10"));
var quanlyTankSnapshot = SourceSnapshotBuilder.Build("quanly", "tank", quanlyTank, Catalog(quanlyCapacity));
Check(quanlyTankSnapshot.GetProperty("deleted_at").ValueKind == JsonValueKind.Null && quanlyTankSnapshot.GetProperty("serial").GetString() == "Q21",
    "The actual Quanly tank schema projects without inventing a deleted_at column");
var quanlyEngine = Row("engines", ("id", "22"), ("name", "Quanly engine"), ("serial", "Q22"), ("old_id", null), ("is_lock", "false"), ("status", "ready"));
Check(SourceSnapshotBuilder.Build("quanly", "engine", quanlyEngine, EmptyCatalog()).GetProperty("deleted_at").ValueKind == JsonValueKind.Null,
    "Quanly engine rows without soft-delete columns retain physical-delete semantics");
Check(SourceSnapshotBuilder.Build("quanly", "tank", Row("tanks", ("id", "21")), EmptyCatalog(), deleting: true).GetProperty("deleted_at").GetString() == "source-delete",
    "Quanly physical delete still produces a tombstone without asset support fields");
try { SourceSnapshotBuilder.Build("quanly", "customer", customer with { Values = customer.Values.Where(pair => pair.Key != "deleted_at").ToDictionary(pair => pair.Key, pair => pair.Value) }, EmptyCatalog()); Check(false, "Customer soft-delete contract cannot be silently weakened"); }
catch (SourceProjectionException error) { Check(error.Code == "missing_column:deleted_at", "Quanly customer/staff soft-delete source columns remain required"); }
var tombstone = SourceSnapshotBuilder.Build("newapp", "tank", Row("Tank", ("Id", "8")), EmptyCatalog(), deleting: true);
Check(tombstone.GetProperty("deleted_at").ValueKind == JsonValueKind.String, "Physical delete/TRUNCATE does not require missing support columns");
var staff = Row("users", ("id", "9"), ("name", "Staff"), ("email", null), ("phone_number", "123456789012345678901"),
    ("is_active", "true"), ("deleted_at", null), ("user_role", "manager"), ("password", "PRIVATE-FIXTURE"), ("api_token", "PRIVATE-FIXTURE"));
var staffSnapshot = SourceSnapshotBuilder.Build("quanly", "user", staff, EmptyCatalog());
Check(!staffSnapshot.TryGetProperty("password", out _) && !staffSnapshot.TryGetProperty("api_token", out _), "Profile adapters never pass credentials or roles");
Check(staffSnapshot.GetProperty("phone_number").GetString()!.Length == 21, "Valid long source phone is preserved without truncation");
var shipper = staff with { Values = staff.Values.ToDictionary(pair => pair.Key, pair => pair.Key == "user_role" ? "shipper" : pair.Value) };
var shipperSnapshot = SourceSnapshotBuilder.Build("quanly", "user", shipper, EmptyCatalog());
Check(shipperSnapshot.GetProperty("deleted_at").ValueKind == JsonValueKind.Null && !shipperSnapshot.TryGetProperty("password", out _) && !shipperSnapshot.TryGetProperty("user_role", out _),
    "Quanly shipper is a staff profile while source credentials and authorization roles remain excluded");
var excluded = staff with { Values = staff.Values.ToDictionary(x => x.Key, x => x.Key == "user_role" ? "customer" : x.Value) };
Check(SourceSnapshotBuilder.Build("quanly", "user", excluded, EmptyCatalog()).GetProperty("deleted_at").GetString() == "not-staff-profile", "Customer/device identities cannot create Core employees");
Check(SourceSnapshotBuilder.Entity("newapp", "Engine") is null && SourceSnapshotBuilder.IsDirect("newapp", "Engine"), "NEWAPP engine remains owned by its direct stream");
Check(SourceSnapshotBuilder.Entity("quanly", "engines") == "engine", "Quanly engine belongs to the raw bridge");
var keylessFirst = Row("move_tanks", ("id", "1"));
var keylessOther = Row("move_tanks", ("id", "2"));
var keylessInvalid = Row("move_tanks", ("id", null));
var keylessCatalog = Catalog(keylessFirst, keylessFirst, keylessOther, keylessInvalid);
Check(keylessCatalog.Find("move_tanks", 2, "id") == keylessOther, "A duplicate or invalid keyless identity cannot prevent an unrelated verified ID from projecting");
try { keylessCatalog.Find("move_tanks", 1, "id"); Check(false, "Duplicate keyless source ID must not select an arbitrary row"); }
catch (SourceProjectionException error) { Check(error.Code == "duplicate_source_identity:move_tanks", "Duplicate keyless native identities remain explicit per-ID conflicts"); }
var tank = Row("Tank", ("Id", "10"), ("Serial", "T10"), ("OldId", null), ("Status", "6"), ("CapacityId", "1"), ("IsDeleted", "false"), ("RFID", "FIXTURE-RFID"));
var capacity = Row("Capacity", ("Id", "1"), ("Name", "10L"), ("Value", "10"));
var warehouse = Row("Warehouse", ("Id", "3"), ("Name", "Warehouse"), ("IsDeleted", "false"));
var cycle = Row("TankCycle", ("Id", "20"), ("EndedTime", null), ("IsDeleted", "false"));
var association = Row("CustomerTank", ("Id", "30"), ("TankId", "10"), ("CustomerId", "7"), ("TankCycleId", "20"), ("ReturnedTime", null));
var warehouseAssociation = Row("WarehouseTank", ("Id", "40"), ("TankId", "10"), ("WarehouseId", "3"), ("TankCycleId", "20"), ("MoveOutTime", null));
var activeCatalog = Catalog(tank, capacity, warehouse, cycle, association, warehouseAssociation);
var tankSnapshot = SourceSnapshotBuilder.Build("newapp", "tank", tank, activeCatalog);
Check(tankSnapshot.GetProperty("customer_id").GetInt64() == 7 && tankSnapshot.GetProperty("status").GetString() == "occupied", "Tank assignment uses active source cycle joins");
Check(tankSnapshot.GetProperty("_source_status").GetInt64() == 6, "Tank snapshots retain the original numeric source workflow code");
var tankSourceStatus = SourceTankStatusPresentation.FromSnapshot("newapp.plasmamed.vn", tankSnapshot.GetRawText());
Check(tankSourceStatus.Code == "6" && tankSourceStatus.Label == "Tại khách hàng", "Per-alias status presentation retains original code and readable label");
var oldStatus = SourceTankStatusPresentation.FromSnapshot("newapp.plasmamed.vn", "{\"status\":\"shipping\"}");
Check(oldStatus.Code is null && oldStatus.Label == "Đang vận chuyển", "Older normalized snapshots show a label without inventing an original numeric code");
var quanlyStatus = SourceTankStatusPresentation.FromSnapshot("quanly.plasma.vn", "{\"status\":\"moving\"}");
Check(quanlyStatus.Code == "moving" && quanlyStatus.Label == "Đang điều chuyển", "Quanly retains its original text workflow code");
Check(SourceTankStatusPresentation.FromSnapshot("newapp.plasmamed.vn", "not-json") == (null, null), "Malformed historical alias metadata never breaks native detail reads");
Check(tankSnapshot.GetProperty("_capacity").GetProperty("value").GetDecimal() == 10 && tankSnapshot.GetProperty("_warehouse").GetProperty("name").GetString() == "Warehouse", "Tank capacity and warehouse are resolved metadata, not numeric display names");
var second = association with { Values = association.Values.ToDictionary(x => x.Key, x => x.Key == "CustomerId" ? "8" : x.Value) };
try { SourceSnapshotBuilder.Build("newapp", "tank", tank, Catalog(tank, capacity, warehouse, cycle, association, second, warehouseAssociation)); Check(false, "Ambiguous assignment rejected"); }
catch (SourceProjectionException error) { Check(error.Code == "ambiguous_tank_assignment", "Ambiguous assignments remain durable conflicts"); }
var ended = cycle with { Values = cycle.Values.ToDictionary(x => x.Key, x => x.Key == "EndedTime" ? "2026-11-09 00:00:00" : x.Value) };
var endedSnapshot = SourceSnapshotBuilder.Build("newapp", "tank", tank, Catalog(tank, capacity, warehouse, ended, association, warehouseAssociation));
Check(endedSnapshot.GetProperty("customer_id").ValueKind == JsonValueKind.Null, "Ended tank cycles cannot assign a current customer");
var install = Guid.NewGuid(); var generation = Guid.NewGuid();
var hash = SourceProjectionIdentity.Hash(customerSnapshot);
var identity = SourceProjectionIdentity.EventId("quanly", install, generation, "100", "customer", 7, hash);
Check(identity == SourceProjectionIdentity.EventId("quanly", install, generation, "100", "customer", 7, hash), "Projection retries use stable immutable event IDs");
Check(identity != SourceProjectionIdentity.EventId("quanly", install, generation, "101", "customer", 7, hash), "A later A-to-B-to-A revision is not mistaken for an old retry");
Check(SourceProjectionIdentity.Namespace("quanly") == "plasma-bv" && SourceProjectionIdentity.SourceSystem("newapp") == "newapp.plasmamed.vn", "Established identity namespaces stay unchanged");
Check((int)OrderStatus.Request == 1 && (int)OrderStatus.ExternalWorkflow == 100 && (int)VoucherStatus.Approved == 3 && (int)VoucherStatus.ExternalWorkflow == 100, "External workflows do not change existing enum values");
Check(NativeBusinessProjector.OrderStatusLabel("newapp", "1") != NativeBusinessProjector.OrderStatusLabel("newapp", "5"), "NEWAPP confirmation is not mislabeled as approval");
Check(NativeBusinessProjector.OrderStatusLabel("newapp", "4") == "Đã đóng", "Closed source orders are not mislabeled as delivered");
Check(NativeBusinessProjector.ParseDate("2026-11-09 12:34:56.123456+00").Kind == DateTimeKind.Utc, "Source timestamp parsing is invariant UTC");
var sensitiveOrder = Row("Order", ("Id", "1"), ("ReceiverInfo", "{\"phone\":\"0900\",\"password\":\"PRIVATE-NESTED\",\"nested\":[{\"access_token\":\"PRIVATE-NESTED\",\"quantity\":2}]}"),
    ("Token", "PRIVATE-TOP"), ("Note", "{malformed PRIVATE-MALFORMED"));
var detail = Row("OrderDetail", ("Id", "1"), ("OrderId", "1"), ("ExtraData", "{\"secret\":\"PRIVATE-CHILD\",\"capacity\":10}"));
var safeOrderCatalog = new SourceProjectionCatalog(new Dictionary<string, IReadOnlyList<SourceSyncRawRow>> { ["OrderDetail"] = [detail], ["CustomerTank"] = [], ["OrderShipping"] = [], ["OrderWarehouse"] = [] });
var safeOrderSnapshot = new NativeBusinessProjector(null!, null!).Snapshot("newapp", "order", sensitiveOrder, safeOrderCatalog, false);
Check(!safeOrderSnapshot.GetRawText().Contains("PRIVATE-", StringComparison.Ordinal) && safeOrderSnapshot.GetRawText().Contains("quantity", StringComparison.Ordinal), "Native order details recursively redact structured credentials and malformed JSON while retaining business facts");
Check(sensitiveOrder.Values["ReceiverInfo"]!.Contains("PRIVATE-NESTED", StringComparison.Ordinal), "Sanitized business details never mutate private full-fidelity raw values");
await using (var model = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=127.0.0.1;Database=never_connect;Encrypt=False").Options))
{
    var sql = model.Database.GenerateCreateScript();
    Check(sql.Contains("[SourceDetailsJson] nvarchar(max)") && sql.Contains("[SourceRecordId] nvarchar(100)"), "Fresh SQL schema includes native source workflow fields");
    Check(model.Model.FindEntityType(typeof(Employee))!.FindProperty(nameof(Employee.Phone))!.GetMaxLength() == 100, "Native employee phone storage matches bridge bound");
    var transferNoteProperty = model.Model.FindEntityType(typeof(TankTransfer))!.FindProperty(nameof(TankTransfer.Note))!;
    Check(transferNoteProperty.GetMaxLength() is null && transferNoteProperty.GetColumnType() == "nvarchar(max)",
        "Source transfer Note storage has no inherited voucher width limit on SQL Server");
}
await using (var model = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=127.0.0.1;Database=never_connect;Username=not_used;Password=not_used").Options))
{
    var note = model.Model.FindEntityType(typeof(TankTransfer))!.FindProperty(nameof(TankTransfer.Note))!;
    Check(note.GetMaxLength() is null && note.GetColumnType() == "text", "Source transfer Note remains provider-neutral PostgreSQL text");
}

var sqlConnection = Environment.GetEnvironmentVariable("SOURCE_PROJECTION_SQL_TEST_CONNECTION");
if (!string.IsNullOrEmpty(sqlConnection))
{
    var parsed = new SqlConnectionStringBuilder(sqlConnection);
    if (!parsed.InitialCatalog.Equals("core_source_sync_validation_20261109", StringComparison.OrdinalIgnoreCase) ||
        !(parsed.DataSource.StartsWith("127.0.0.1", StringComparison.Ordinal) || parsed.DataSource.StartsWith("localhost", StringComparison.Ordinal)))
        throw new InvalidOperationException("Only a local, explicitly named isolated source-sync fixture is accepted.");
    await using (var guard = new SqlConnection(sqlConnection))
    {
        await guard.OpenAsync();
        await using var command = guard.CreateCommand();
        command.CommandText = "SELECT CONVERT(nvarchar(100),value) FROM sys.extended_properties WHERE class=0 AND name=N'core_source_sync_validation_owner'";
        if (await command.ExecuteScalarAsync() as string != "core-source-sync-validation-v1")
            throw new InvalidOperationException("Fixture ownership marker is missing; refusing native test writes.");
    }
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(sqlConnection));
    serviceCollection.AddSingleton<IOptionsMonitor<LegacySyncSettings>>(new FixedOptions<LegacySyncSettings>(new LegacySyncSettings()));
    serviceCollection.AddSingleton(DispatchProxy.Create<IPermissionService, PermissionStub>());
    serviceCollection.AddScoped<ILegacyEventIngestService, LegacyEventIngestService>();
    serviceCollection.AddScoped<NativeProfileProjector>(); serviceCollection.AddScoped<NativeBusinessProjector>();
    serviceCollection.AddScoped<NativeTankEngineHistoryProjector>();
    await using var provider = serviceCollection.BuildServiceProvider();
    async Task<T> Scoped<T>(Func<IServiceProvider, Task<T>> action) { await using var scope = provider.CreateAsyncScope(); return await action(scope.ServiceProvider); }
    var id = 8_000_000_000 + Random.Shared.NextInt64(1, 900_000_000);
    var baselineCustomerId = id + 20;
    var baselineCustomer = JsonSerializer.SerializeToElement(new { id = baselineCustomerId, name = "Previously missing source clinic", address = (string?)null,
        phone_number = (string?)null, status = "active", deleted_at = (string?)null });
    var baselineCreated = await Scoped(services => services.GetRequiredService<NativeProfileProjector>().ApplyAsync("newapp", install, generation, "baseline-v2", "customer",
        baselineCustomerId, baselineCustomer, "create", DateTimeOffset.UnixEpoch, default));
    var baselineRetried = await Scoped(services => services.GetRequiredService<NativeProfileProjector>().ApplyAsync("newapp", install, generation, "baseline-v2", "customer",
        baselineCustomerId, baselineCustomer, "create", DateTimeOffset.UnixEpoch, default));
    Check(baselineCreated.State == "applied" && baselineCreated.CoreId == baselineRetried.CoreId && await Scoped(services => services.GetRequiredService<ApplicationDbContext>().Customers
        .AnyAsync(record => record.Id == baselineCreated.CoreId && record.NewAppId == baselineCustomerId)), "A previously missing baseline customer is created once with stable source-owned identity");

    var baselineTankId = id + 21;
    JsonElement TankData(long sourceId, string serial, string? oldId = null) => JsonSerializer.SerializeToElement(new { id = sourceId, serial, old_id = oldId,
        status = "ready", customer_id = (long?)null, _source_status = 0, _capacity = new { id = 1, name = "Fixture 10L", value = 10 }, _warehouse = (object?)null,
        _factory = (object?)null, deleted_at = (string?)null });
    var baselineTank = TankData(baselineTankId, "NEW-BASELINE-" + baselineTankId);
    var createdTank = await Scoped(services => services.GetRequiredService<NativeProfileProjector>().ApplyAsync("newapp", install, generation, "baseline-v2", "tank",
        baselineTankId, baselineTank, "create", DateTimeOffset.UnixEpoch, default));
    Check(createdTank.State == "applied" && await Scoped(services => services.GetRequiredService<ApplicationDbContext>().Tanks.AnyAsync(record => record.Id == createdTank.CoreId && record.NewAppId == baselineTankId)),
        "A previously missing baseline tank is created through the existing collision-checked native handler");
    var collisionSourceId = id + 22;
    try { await Scoped(services => services.GetRequiredService<NativeProfileProjector>().ApplyAsync("newapp", install, generation, "baseline-v2", "tank", collisionSourceId,
        TankData(collisionSourceId, "NEW-BASELINE-" + baselineTankId), "create", DateTimeOffset.UnixEpoch, default)); Check(false, "Serial collision cannot bind a second source identity"); }
    catch (ConflictException) { Check(await Scoped(async services => { var db = services.GetRequiredService<ApplicationDbContext>(); return !await db.LegacySyncMappings.AnyAsync(mapping => mapping.Source == "newapp" && mapping.Entity == "tank" && mapping.EntityId == collisionSourceId)
        && !await db.Tanks.AnyAsync(record => record.NewAppId == collisionSourceId); }), "Known serial collision stays blocked and never merges or overwrites an existing tank"); }
    var explicitCoreId = Guid.NewGuid(); var explicitSourceId = id + 23; var explicitLegacyId = Guid.NewGuid().ToString("N")[..24];
    await Scoped(async services => { var db = services.GetRequiredService<ApplicationDbContext>(); db.Tanks.Add(new Tank { Id = explicitCoreId, TankCode = "EXPLICIT-" + explicitSourceId,
        SerialNumber = "EXPLICIT-" + explicitSourceId, LegacyId = explicitLegacyId, CreatedDate = DateTime.UtcNow }); await db.SaveChangesAsync(); return true; });
    var explicitlyBound = await Scoped(services => services.GetRequiredService<NativeProfileProjector>().ApplyAsync("newapp", install, generation, "baseline-v2", "tank", explicitSourceId,
        TankData(explicitSourceId, "EXPLICIT-" + explicitSourceId, explicitLegacyId), "create", DateTimeOffset.UnixEpoch, default));
    Check(explicitlyBound.CoreId == explicitCoreId && await Scoped(services => services.GetRequiredService<ApplicationDbContext>().LegacySyncMappings.AnyAsync(mapping =>
        mapping.Source == "newapp" && mapping.Entity == "tank" && mapping.EntityId == explicitSourceId && mapping.CoreId == explicitCoreId)), "Explicit source OldId can bind a unique existing PLT ObjectId while retaining its Core GUID");
    var guidEngineCoreId = Guid.NewGuid(); var guidEngineSourceId = id + 24; var guidLegacyId = Guid.NewGuid();
    await Scoped(async services => { var db = services.GetRequiredService<ApplicationDbContext>(); db.Engines.Add(new Engine { Id = guidEngineCoreId, EngineCode = "GUID-" + guidEngineSourceId,
        EngineName = "GUID fixture", SerialNumber = "GUID-" + guidEngineSourceId, LegacyId = guidLegacyId.ToString("D").ToUpperInvariant(), CreatedDate = DateTime.UtcNow }); await db.SaveChangesAsync(); return true; });
    var guidEngineSnapshot = JsonSerializer.SerializeToElement(new { id = guidEngineSourceId, name = "GUID fixture", serial = "GUID-" + guidEngineSourceId,
        old_id = guidLegacyId.ToString("N"), status = "active", deleted_at = (string?)null });
    var guidBound = await Scoped(services => services.GetRequiredService<NativeProfileProjector>().ApplyAsync("quanly", install, generation, "baseline-v2", "engine",
        guidEngineSourceId, guidEngineSnapshot, "create", DateTimeOffset.UnixEpoch, default));
    Check(guidBound.CoreId == guidEngineCoreId, "A strict explicit GUID alias binds across canonical D/N formatting without using serial as authority");
    var coreCustomer = Guid.NewGuid();
    await Scoped(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        db.Customers.Add(new Customer { Id = coreCustomer, CustomerCode = "FIXTURE-" + id, CustomerName = "Before", NewAppId = id, CreatedDate = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await services.GetRequiredService<ILegacyEventIngestService>().BindMappingAsync(new() { Source = "newapp", SourceSystem = "newapp.plasmamed.vn", Entity = "customer", EntityId = id, CoreId = coreCustomer }, default);
        return true;
    });
    JsonElement CustomerData(string name) => JsonSerializer.SerializeToElement(new { id, name, address = "Fixture", phone_number = "123456789012345678901", status = "active", deleted_at = (string?)null });
    var bridgeEvent = new LegacyEventIngestDto { EventId = Guid.NewGuid(), Source = "newapp", SourceSystem = "newapp.plasmamed.vn", Entity = "customer", EntityId = id,
        Operation = "update", OccurredAt = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero), Data = CustomerData("After") };
    var bridge = await Scoped(services => services.GetRequiredService<ILegacyEventIngestService>().IngestProjectionAsync(bridgeEvent, default));
    Check(bridge.Status == "applied", "Internal native bridge applies a verified customer");
    Check(await Scoped(async services => { var db = services.GetRequiredService<ApplicationDbContext>(); return await db.Customers.AnyAsync(x => x.Id == coreCustomer && x.CustomerName == "After") &&
        await db.LegacySyncReceipts.AnyAsync(x => x.EventId == bridgeEvent.EventId && x.Source == "raw-newapp" && x.Sequence == bridge.Sequence) &&
        await db.LegacySyncMappings.AnyAsync(x => x.Source == "newapp" && x.Entity == "customer" && x.EntityId == id && x.LastSequence == bridge.Sequence); }), "Receipt sequence namespace is separate while canonical mapping namespace is retained");
    var retry = await Scoped(services => services.GetRequiredService<ILegacyEventIngestService>().IngestProjectionAsync(bridgeEvent, default));
    Check(retry.Status == "duplicate" && retry.Sequence == bridge.Sequence, "Lost-ack retry reuses the allocated native sequence");
    var originalData = bridgeEvent.Data;
    bridgeEvent.Data = CustomerData("Tampered retry");
    try { await Scoped(services => services.GetRequiredService<ILegacyEventIngestService>().IngestProjectionAsync(bridgeEvent, default)); Check(false, "Changed immutable retry must fail"); }
    catch (ConflictException) { Check(true, "A reused raw projection event ID cannot change its immutable native payload"); }
    bridgeEvent.Data = originalData;
    var invalidEvent = new LegacyEventIngestDto { EventId = Guid.NewGuid(), Source = "newapp", SourceSystem = "newapp.plasmamed.vn", Entity = "customer", EntityId = id,
        Operation = "update", OccurredAt = bridgeEvent.OccurredAt, Data = CustomerData(new string('X', 251)) };
    try { await Scoped(services => services.GetRequiredService<ILegacyEventIngestService>().IngestProjectionAsync(invalidEvent, default)); Check(false, "Invalid full snapshot must fail"); }
    catch (BusinessException) { Check(await Scoped(async services => { var db = services.GetRequiredService<ApplicationDbContext>(); return !await db.LegacySyncReceipts.AnyAsync(x => x.EventId == invalidEvent.EventId) &&
        await db.Customers.AnyAsync(x => x.Id == coreCustomer && x.CustomerName == "After"); }), "Invalid bridge snapshot rolls back receipt, sequence, mapping and native business mutation"); }
    var engineId = id + 1;
    var direct = new LegacyEventIngestDto { EventId = Guid.NewGuid(), Source = "newapp", SourceSystem = "newapp.plasmamed.vn", Entity = "engine", EntityId = engineId,
        Sequence = bridge.Sequence, Operation = "create", OccurredAt = bridgeEvent.OccurredAt,
        Data = JsonSerializer.SerializeToElement(new { id = engineId, name = "Fixture", serial = "FIXTURE-" + engineId, old_id = (string?)null, status = "active", deleted_at = (string?)null }) };
    var directResult = await Scoped(services => services.GetRequiredService<ILegacyEventIngestService>().IngestAsync(direct, default));
    Check(directResult.Status == "applied", "Direct engine sender may use the same numeric sequence without a raw receipt collision");
    try { await Scoped(services => services.GetRequiredService<ILegacyEventIngestService>().IngestProjectionAsync(direct, default)); Check(false, "Direct engine ownership must reject raw writer"); }
    catch (ConflictException) { Check(true, "Raw bridge cannot become a second NEWAPP engine writer"); }
    await Scoped(async services => { await services.GetRequiredService<ILegacyEventIngestService>().BindMappingAsync(new() { Source = "plasma-bv", SourceSystem = "quanly.plasma.vn", Entity = "customer", EntityId = id, CoreId = coreCustomer }, default); return true; });
    var protectedEvent = new LegacyEventIngestDto { EventId = Guid.NewGuid(), Source = "newapp", SourceSystem = "newapp.plasmamed.vn", Entity = "customer", EntityId = id,
        Operation = "update", OccurredAt = bridgeEvent.OccurredAt, Data = CustomerData("Conflicting alias") };
    var protectedResult = await Scoped(services => services.GetRequiredService<ILegacyEventIngestService>().IngestProjectionAsync(protectedEvent, default));
    Check(protectedResult.Status == "protected" && await Scoped(services => services.GetRequiredService<ApplicationDbContext>().Customers.AnyAsync(x => x.Id == coreCustomer && x.CustomerName == "After")), "Merged customer snapshots are retained without silently overwriting canonical business fields");
    var protectedRetry = await Scoped(services => services.GetRequiredService<ILegacyEventIngestService>().IngestProjectionAsync(protectedEvent, default));
    Check(protectedRetry.Status == "protected", "A protected retry never falsely becomes native-applied");

    var staffId = id + 3;
    var staffEvent = new LegacyEventIngestDto { EventId = Guid.NewGuid(), Source = "newapp", SourceSystem = "newapp.plasmamed.vn", Entity = "user", EntityId = staffId,
        Operation = "create", OccurredAt = bridgeEvent.OccurredAt, Data = JsonSerializer.SerializeToElement(new { id = staffId, name = "Fixture staff", email = (string?)null,
            phone_number = "123456789012345678901", is_active = true, user_type = 1, deleted_at = (string?)null }) };
    await Scoped(services => services.GetRequiredService<ILegacyEventIngestService>().IngestProjectionAsync(staffEvent, default));
    Check(await Scoped(async services => { var db = services.GetRequiredService<ApplicationDbContext>(); var fixtureEmployee = await db.Employees.SingleAsync(x => x.NewAppId == staffId);
        return fixtureEmployee.Phone!.Length == 21 && !await db.EmployeeUsers.AnyAsync(x => x.EmployeeId == fixtureEmployee.Id); }), "Long staff phone projects without creating a Core login or ACL");

    var orderId = id + 2;
    var orderRow = Row("Order", ("Id", orderId.ToString(CultureInfo.InvariantCulture)), ("CustomerId", id.ToString(CultureInfo.InvariantCulture)), ("CreatedBy", null),
        ("CreatedDate", "2026-11-09 00:00:00"), ("UpdatedDate", null), ("Status", "1"), ("IsDemo", "false"), ("IsDeleted", "false"), ("Note", "Fixture"), ("OldId", null));
    var orderTables = new Dictionary<string, IReadOnlyList<SourceSyncRawRow>> { ["OrderDetail"] = [], ["CustomerTank"] = [], ["OrderShipping"] = [], ["OrderWarehouse"] = [] };
    var orderCatalog = new SourceProjectionCatalog(orderTables);
    var nativeOrder = await Scoped(async services => { var projector = services.GetRequiredService<NativeBusinessProjector>(); return await projector.ApplyAsync("newapp", install, "order", orderRow, orderCatalog,
        projector.Snapshot("newapp", "order", orderRow, orderCatalog, false), false, default); });
    var nativeOrderRetry = await Scoped(async services => { var projector = services.GetRequiredService<NativeBusinessProjector>(); return await projector.ApplyAsync("newapp", install, "order", orderRow, orderCatalog,
        projector.Snapshot("newapp", "order", orderRow, orderCatalog, false), false, default); });
    Check(nativeOrder.CoreId == nativeOrderRetry.CoreId && await Scoped(services => services.GetRequiredService<ApplicationDbContext>().Orders.AnyAsync(x => x.Id == nativeOrder.CoreId && x.SourceManaged &&
        x.Status == OrderStatus.ExternalWorkflow && x.SourceStatusCode == "1" && x.SourceStatusLabel == "Đã xác nhận phân kho")), "Source order upsert preserves GUID and exact external workflow");

    var nativeTankId = id + 4; var tankGuid = Guid.NewGuid();
    await Scoped(async services => { var db = services.GetRequiredService<ApplicationDbContext>(); db.Tanks.Add(new Tank { Id = tankGuid, NewAppId = nativeTankId,
        TankCode = "FIXTURE-" + nativeTankId, Status = TankStatus.InStock, CurrentLocation = "Original stock", CreatedDate = DateTime.UtcNow }); await db.SaveChangesAsync(); return true; });
    var moveId = id + 5;
    var transferRow = Row("MoveWarehouseOrder", ("Id", moveId.ToString(CultureInfo.InvariantCulture)), ("FromId", "1"), ("ToId", "2"),
        ("CreatedDate", "2026-11-09 00:00:00"), ("IsDeleted", "false"));
    var transferItem = Row("MoveWarehouseOrderTank", ("Id", "1"), ("MoveWarehouseOrderId", moveId.ToString(CultureInfo.InvariantCulture)), ("TankId", nativeTankId.ToString(CultureInfo.InvariantCulture)));
    var destination = Row("Warehouse", ("Id", "2"), ("Name", "Destination"), ("IsDeleted", "false"));
    var transferCatalog = Catalog(transferItem, destination);
    var projectedTransfer = await Scoped(async services => { var projector = services.GetRequiredService<NativeBusinessProjector>(); return await projector.ApplyAsync("newapp", install, "transfer", transferRow, transferCatalog,
        projector.Snapshot("newapp", "transfer", transferRow, transferCatalog, false), false, default); });
    Check(await Scoped(async services => { var db = services.GetRequiredService<ApplicationDbContext>(); return await db.TankTransfers.AnyAsync(x => x.Id == projectedTransfer.CoreId && x.SourceManaged && x.Status == VoucherStatus.ExternalWorkflow && x.ToLocation == "Destination") &&
        await db.TankTransferItems.AnyAsync(x => x.TankTransferId == projectedTransfer.CoreId && x.TankId == tankGuid) && await db.Tanks.AnyAsync(x => x.Id == tankGuid && x.Status == TankStatus.InStock && x.CurrentLocation == "Original stock"); }),
        "External transfer projects verified items without executing a second native stock movement");

    var longTransferNote = new string('Đ', 1042);
    var quanlyMoveId = id + 11042;
    var quanlyTransfer = Row("move_tanks", ("id", quanlyMoveId.ToString(CultureInfo.InvariantCulture)),
        ("from_warehouse_id", "1"), ("to_warehouse_id", "2"), ("created_at", "2026-11-09 00:00:00+00"),
        ("status", "confirmed"), ("note", longTransferNote), ("deleted_at", null));
    var quanlyTransferCatalog = new SourceProjectionCatalog(new Dictionary<string, IReadOnlyList<SourceSyncRawRow>>
    {
        ["move_items"] = [], ["move_tank_items"] = [], ["move_tank_shippings"] = [],
        ["warehouses"] = [Row("warehouses", ("id", "2"), ("name", "Đích"))]
    });
    var longNoteProjection = await Scoped(async services =>
    {
        var projector = services.GetRequiredService<NativeBusinessProjector>();
        return await projector.ApplyAsync("quanly", install, "transfer", quanlyTransfer, quanlyTransferCatalog,
            projector.Snapshot("quanly", "transfer", quanlyTransfer, quanlyTransferCatalog, false), false, default);
    });
    Check(longNoteProjection.State == "applied" && await Scoped(async services =>
    {
        var saved = await services.GetRequiredService<ApplicationDbContext>().TankTransfers.AsNoTracking().SingleAsync(row => row.Id == longNoteProjection.CoreId);
        using var details = JsonDocument.Parse(saved.SourceDetailsJson!);
        var sourceNote = details.RootElement.GetProperty("row").GetProperty("note").GetString();
        return saved.Note == longTransferNote && saved.Note.Length == 1042 && sourceNote == longTransferNote && sourceNote.Length == 1042;
    }), "A 1042-character Quanly transfer note is retained exactly in native Note and SourceDetailsJson without truncation");

    var eventHistoryId = id + 6;
    var historyRow = Row("TankEngineHistory", ("Id", eventHistoryId.ToString(CultureInfo.InvariantCulture)), ("EngineId", engineId.ToString(CultureInfo.InvariantCulture)),
        ("RFID", "FIXTURE-RFID"), ("CreateTime", "2026-11-09 01:02:03+00"), ("Type", "0"));
    var sourceTank = Row("Tank", ("Id", nativeTankId.ToString(CultureInfo.InvariantCulture)), ("RFID", "FIXTURE-RFID"));
    var projectedHistory = await Scoped(services => services.GetRequiredService<NativeTankEngineHistoryProjector>().ApplyAsync(historyRow, Catalog(sourceTank), false, default));
    var projectedHistoryRetry = await Scoped(services => services.GetRequiredService<NativeTankEngineHistoryProjector>().ApplyAsync(historyRow, Catalog(sourceTank), false, default));
    Check(projectedHistory.CoreId == projectedHistoryRetry.CoreId && await Scoped(services => services.GetRequiredService<ApplicationDbContext>().TankEngineHistories.AnyAsync(x => x.Id == projectedHistory.CoreId && x.LegacyId == eventHistoryId && x.TankId == tankGuid && x.EventType == 0)),
        "Tank-engine history upserts its owned legacy ID and verified engine/tank aliases");
    Check(await Scoped(async services => { var metadata = new NativeProjectionStore(services.GetRequiredService<ApplicationDbContext>());
        var saved = new NativeProjectionState("customer", id, coreCustomer, new string('A', 64), "protected", "canonical_profile_protected");
        await metadata.SaveAsync("newapp", install, generation, saved, "fixture", default);
        var found = await metadata.FindAsync("newapp", install, generation, "customer", id, default);
        return found == saved && (await metadata.ReadProfilesAsync("newapp", install, generation, default)).Any(item => item == saved);
    }), "Native projection metadata persists protected state and can resume a generation without raw-ID cursors");

    SourceSyncRawRow BackfillHistory(long sourceId) => Row("TankEngineHistory", ("Id", sourceId.ToString(CultureInfo.InvariantCulture)),
        ("EngineId", engineId.ToString(CultureInfo.InvariantCulture)), ("RFID", null), ("CreateTime", "2026-11-09 02:03:04+00"), ("Type", "0"));
    await using var genericServices = new ServiceCollection().AddSingleton<ISourceBusinessProjector>(new BackfillBusinessStub()).BuildServiceProvider();
    Task RunBackfill(BackfillRawStub reader, SourceProjectionOptions settings) => Scoped(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var runner = new SourceProjectionRunner(db, reader, new NativeProjectionStore(db), services.GetRequiredService<NativeProfileProjector>(),
            services.GetRequiredService<NativeBusinessProjector>(), services.GetRequiredService<NativeTankEngineHistoryProjector>(),
            genericServices, new FixedOptions<SourceProjectionOptions>(settings), NullLogger<SourceProjectionRunner>.Instance);
        await runner.RunOnceAsync(default); return true;
    });
    Task<NativeProjectionCheckpoint?> BackfillCheckpoint(BackfillRawStub reader) => Scoped(services =>
        new NativeProjectionStore(services.GetRequiredService<ApplicationDbContext>()).CheckpointAsync("newapp", reader.Installation, reader.Generation, "_history_baseline", default));
    var boundedSettings = new SourceProjectionOptions { Enabled = true, Sources = ["newapp"], TankEngineHistoryEnabled = true,
        PolicyVersion = "fixture-bounded-" + id, HistoryBaselineMaxRowsPerPoll = 1, HistoryBaselineMaxSeconds = 60 };
    var historyCandidates = new[] { BackfillHistory(id + 100), BackfillHistory(id + 101), BackfillHistory(id + 102), BackfillHistory(id + 103) }
        .OrderBy(BackfillRawStub.Key, StringComparer.Ordinal).ToArray();
    var backfill = new BackfillRawStub(historyCandidates.Skip(1).ToList());
    await RunBackfill(backfill, boundedSettings);
    var firstCheckpoint = await BackfillCheckpoint(backfill);
    Check(firstCheckpoint is { State: "blocked", ErrorCode: "history_baseline_in_progress" } && firstCheckpoint.Revision.Length == 44 && backfill.LastBaselineState == "blocked",
        "A bounded history slice persists an exact key-hash cursor and never reports incomplete baseline as applied");
    var processedKey = firstCheckpoint!.Revision;
    var lateRow = historyCandidates[0];
    var lateId = long.Parse(lateRow.Values["Id"]!, CultureInfo.InvariantCulture);
    backfill.History.Add(lateRow);
    var lateEvent = new SourceSyncEvent("1", "1", "TankEngineHistory", "INSERT", JsonSerializer.SerializeToElement<object?>(null),
        JsonSerializer.SerializeToElement(lateRow.Values), "2026-11-09T02:03:04Z");
    backfill.Pending.Add(new("newapp", backfill.Installation, backfill.Generation, "1", lateEvent, "pending"));
    await RunBackfill(backfill, boundedSettings);
    Check(StringComparer.Ordinal.Compare(BackfillRawStub.Key(lateRow), Convert.ToHexString(Convert.FromBase64String(processedKey))) < 0 &&
        backfill.EventStates.GetValueOrDefault("1") == "applied" && (await BackfillCheckpoint(backfill))?.ErrorCode == "history_baseline_in_progress" &&
        await Scoped(services => services.GetRequiredService<ApplicationDbContext>().TankEngineHistories.AnyAsync(record => record.LegacyId == lateId)),
        "A new history row behind the baseline cursor is applied by its live receipt while baseline remains incomplete");
    var callsBeforeIdle = backfill.PageCalls;
    await RunBackfill(backfill, boundedSettings);
    Check((await BackfillCheckpoint(backfill))?.State == "applied" && backfill.LastBaselineState == "applied" && backfill.PageCalls > callsBeforeIdle &&
        backfill.SnapshotRequests.Last().SequenceEqual(new[] { "Tank" }),
        "An idle poll continues unfinished history using only its Tank reference catalog and completes truthfully");
    var completedCalls = backfill.PageCalls; var completedSnapshots = backfill.SnapshotRequests.Count;
    await RunBackfill(backfill, boundedSettings);
    Check(backfill.PageCalls == completedCalls && backfill.SnapshotRequests.Count == completedSnapshots,
        "An idle completed history baseline performs no repeated full scan");

    var crashing = new BackfillRawStub([BackfillHistory(id + 400), BackfillHistory(id + 401), BackfillHistory(id + 402)])
        { PageSizeOverride = 1, FailOnPageCall = 2 };
    var crashSettings = new SourceProjectionOptions { Enabled = true, Sources = ["newapp"], TankEngineHistoryEnabled = true,
        PolicyVersion = "fixture-crash-" + id, HistoryBaselineMaxRowsPerPoll = 2, HistoryBaselineMaxSeconds = 60 };
    try { await RunBackfill(crashing, crashSettings); Check(false, "Injected crash after first durable row must escape the slice"); }
    catch (BackfillFixtureCrashException) { Check(await BackfillCheckpoint(crashing) is null,
        "A crash before cursor checkpoint does not advance or falsely complete history baseline"); }
    var firstCrashId = long.Parse(crashing.OrderedHistory()[0].Values["Id"]!, CultureInfo.InvariantCulture);
    var firstCrashGuid = await Scoped(services => services.GetRequiredService<ApplicationDbContext>().TankEngineHistories
        .Where(record => record.LegacyId == firstCrashId).Select(record => record.Id).SingleAsync());
    crashing.FailOnPageCall = null;
    await RunBackfill(crashing, crashSettings);
    Check(await Scoped(services => services.GetRequiredService<ApplicationDbContext>().TankEngineHistories.CountAsync(record =>
        record.LegacyId == firstCrashId && record.Id == firstCrashGuid)) == 1 && (await BackfillCheckpoint(crashing))?.ErrorCode == "history_baseline_in_progress",
        "Crash replay preserves the existing Core history GUID and resumes without duplicate inserts");
    await RunBackfill(crashing, crashSettings);
    Check((await BackfillCheckpoint(crashing))?.State == "applied", "Replayed history finishes in later bounded slices with no pending source events");

    var timed = new BackfillRawStub([BackfillHistory(id + 500), BackfillHistory(id + 501), BackfillHistory(id + 502)]) { DelayNextPageMilliseconds = 1100 };
    var timedSettings = new SourceProjectionOptions { Enabled = true, Sources = ["newapp"], TankEngineHistoryEnabled = true,
        PolicyVersion = "fixture-time-" + id, HistoryBaselineMaxRowsPerPoll = 200, HistoryBaselineMaxSeconds = 1 };
    await RunBackfill(timed, timedSettings);
    Check((await BackfillCheckpoint(timed))?.ErrorCode == "history_baseline_in_progress" && timed.LastBaselineState == "blocked",
        "A time limit within the final raw page does not discard its unprocessed rows or declare completion");
    timedSettings.HistoryBaselineMaxSeconds = 60;
    await RunBackfill(timed, timedSettings);
    Check((await BackfillCheckpoint(timed))?.State == "applied" && await Scoped(services => services.GetRequiredService<ApplicationDbContext>().TankEngineHistories
        .CountAsync(record => record.LegacyId == id + 500 || record.LegacyId == id + 501 || record.LegacyId == id + 502)) == 3,
        "A partial-page cursor resumes exactly after the last durable row and eventually projects every history record");

    var adapterRaw = new BackfillRawStub([]) { Customer = Row("Customer", ("Id", baselineCustomerId.ToString(CultureInfo.InvariantCulture)),
        ("Name", "Previously missing source clinic"), ("Address", null), ("Tel", null), ("IsDeleted", "false")) };
    var adapterSettings = new SourceProjectionOptions { Enabled = true, Sources = ["newapp"], PolicyVersion = "fixture-adapter-" + id };
    var adapterSnapshot = SourceSnapshotBuilder.Build("newapp", "customer", adapterRaw.Customer, EmptyCatalog());
    var oldCheckpointHash = SourceProjectionIdentity.BaselineHash(adapterRaw.Generation, adapterSettings, "old-source-contract");
    var recordHash = SourceProjectionIdentity.RecordHash(adapterSettings.PolicyVersion, adapterSnapshot);
    var originalSequence = await Scoped(services => services.GetRequiredService<ApplicationDbContext>().LegacySyncMappings
        .Where(mapping => mapping.Source == "newapp" && mapping.Entity == "customer" && mapping.EntityId == baselineCustomerId).Select(mapping => mapping.LastSequence).SingleAsync());
    await Scoped(async services =>
    {
        var metadata = new NativeProjectionStore(services.GetRequiredService<ApplicationDbContext>());
        await metadata.SaveAsync("newapp", adapterRaw.Installation, adapterRaw.Generation,
            new("customer", baselineCustomerId, baselineCreated.CoreId, recordHash, "applied", null), "fixture", default);
        await metadata.SaveAsync("newapp", adapterRaw.Installation, adapterRaw.Generation,
            new("_profile_baseline", 1, null, oldCheckpointHash, "applied", null), "fixture", default);
        return true;
    });
    await RunBackfill(adapterRaw, adapterSettings);
    Check(await Scoped(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var marker = await new NativeProjectionStore(db).FindAsync("newapp", adapterRaw.Installation, adapterRaw.Generation, "_profile_baseline", 1, default);
        var sequence = await db.LegacySyncMappings.Where(mapping => mapping.Source == "newapp" && mapping.Entity == "customer" && mapping.EntityId == baselineCustomerId)
            .Select(mapping => mapping.LastSequence).SingleAsync();
        return marker?.SnapshotHash == SourceProjectionIdentity.BaselineHash(adapterRaw.Generation, adapterSettings)
            && marker.SnapshotHash != oldCheckpointHash && sequence == originalSequence;
    }), "An adapter checkpoint version rescans the source but unchanged applied records keep their native sequence and skip replay");
    var shipperSourceId = id + 600;
    var shipperRow = shipper with { Values = shipper.Values.ToDictionary(pair => pair.Key, pair => pair.Key == "id" ? shipperSourceId.ToString(CultureInfo.InvariantCulture) : pair.Value) };
    var nativeShipper = await Scoped(services => services.GetRequiredService<NativeProfileProjector>().ApplyAsync("quanly", install, generation, "fixture-shipper", "user",
        shipperSourceId, SourceSnapshotBuilder.Build("quanly", "user", shipperRow, EmptyCatalog()), "create", DateTimeOffset.UnixEpoch, default));
    Check(nativeShipper.State == "applied" && await Scoped(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        return await db.Employees.AnyAsync(employee => employee.Id == nativeShipper.CoreId && employee.QuanLyId == shipperSourceId)
            && !await db.EmployeeUsers.AnyAsync(link => link.EmployeeId == nativeShipper.CoreId);
    }), "Quanly shipper creates only a descriptive employee profile without a Core login or ACL grant");

    // Reproduce the real crash gap: Apply commits its ownership/native row,
    // but no NativeProjection metadata is written before a later TRUNCATE.
    var truncating = new BackfillRawStub([]) { Installation = install };
    truncating.AdditionalTables.AddRange([
        new("Customer", ["Id"], ["Id"], "fixture-empty-customers"),
        new("Order", ["Id"], ["Id"], "fixture-empty-orders"),
        new("MoveWarehouseOrder", ["Id"], ["Id"], "fixture-empty-transfers")]);
    var gapCustomerId = id + 700;
    var gapCustomer = await Scoped(services => services.GetRequiredService<NativeProfileProjector>().ApplyAsync("newapp", install, truncating.Generation,
        "fixture-before-metadata", "customer", gapCustomerId, JsonSerializer.SerializeToElement(new { id = gapCustomerId, name = "Native before metadata",
            address = (string?)null, phone_number = (string?)null, status = "active", deleted_at = (string?)null }), "create", DateTimeOffset.UnixEpoch, default));
    var otherSourceId = id + 701;
    var otherSource = await Scoped(services => services.GetRequiredService<NativeProfileProjector>().ApplyAsync("quanly", install, truncating.Generation,
        "fixture-other-source", "customer", otherSourceId, JsonSerializer.SerializeToElement(new { id = otherSourceId, name = "Other source native",
            address = (string?)null, phone_number = (string?)null, status = "active", deleted_at = (string?)null }), "create", DateTimeOffset.UnixEpoch, default));
    var coreOnlyCustomerId = Guid.NewGuid(); var coreOnlyHistoryId = Guid.NewGuid();
    await Scoped(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var engineCore = await db.Engines.Where(engine => engine.NewAppId == engineId).Select(engine => engine.Id).SingleAsync();
        db.Customers.Add(new Customer { Id = coreOnlyCustomerId, CustomerCode = "CORE-ONLY-" + id,
            CustomerName = "Core-only customer", CreatedDate = DateTime.UtcNow });
        db.TankEngineHistories.Add(new TankEngineHistory { Id = coreOnlyHistoryId, EngineId = engineCore, LegacyId = null,
            EventTime = DateTime.UtcNow, EventType = 0, CreatedDate = DateTime.UtcNow });
        await db.SaveChangesAsync(); return true;
    });
    var otherInstallationOrderId = id + 702;
    var otherInstallationRow = orderRow with { Values = orderRow.Values.ToDictionary(pair => pair.Key,
        pair => pair.Key == "Id" ? otherInstallationOrderId.ToString(CultureInfo.InvariantCulture) : pair.Value) };
    var otherInstallationOrder = await Scoped(async services =>
    {
        var projector = services.GetRequiredService<NativeBusinessProjector>();
        return await projector.ApplyAsync("newapp", Guid.NewGuid(), "order", otherInstallationRow, orderCatalog,
            projector.Snapshot("newapp", "order", otherInstallationRow, orderCatalog, false), false, default);
    });
    Check(await Scoped(async services =>
    {
        var metadata = new NativeProjectionStore(services.GetRequiredService<ApplicationDbContext>());
        return await metadata.FindAsync("newapp", install, truncating.Generation, "customer", gapCustomerId, default) is null
            && await metadata.FindAsync("newapp", install, truncating.Generation, "order", orderId, default) is null
            && await metadata.FindAsync("newapp", install, truncating.Generation, "transfer", moveId, default) is null
            && await metadata.FindAsync("newapp", install, truncating.Generation, "tank-engine-history", eventHistoryId, default) is null;
    }), "Committed profile/order/transfer/history fixture rows have no projection metadata, reproducing the crash gap");
    var truncateTables = new[] { "Customer", "Order", "MoveWarehouseOrder", "TankEngineHistory" };
    for (var index = 0; index < truncateTables.Length; index++)
    {
        var eventId = (11 + index).ToString(CultureInfo.InvariantCulture);
        truncating.Pending.Add(new("newapp", install, truncating.Generation, eventId,
            new(eventId, "1", truncateTables[index], "TRUNCATE", JsonSerializer.SerializeToElement<object?>(null),
                JsonSerializer.SerializeToElement<object?>(null), "2026-11-09T03:04:05Z"), "pending"));
    }
    var truncateSettings = new SourceProjectionOptions { Enabled = true, Sources = ["newapp"], OrdersEnabled = true, TransfersEnabled = true,
        TankEngineHistoryEnabled = true, PolicyVersion = "fixture-truncate-" + id };
    await RunBackfill(truncating, truncateSettings);
    Check(await Scoped(services => services.GetRequiredService<ApplicationDbContext>().Customers.IgnoreQueryFilters()
        .AnyAsync(customer => customer.Id == gapCustomer.CoreId && customer.IsDeleted)),
        "TRUNCATE discovers a committed profile from its verified source mapping even when metadata was never saved");
    Check(truncating.EventStates.GetValueOrDefault("11") == "blocked" && await Scoped(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        return await db.Customers.AnyAsync(customer => customer.Id == coreCustomer)
            && await db.Customers.AnyAsync(customer => customer.Id == otherSource.CoreId)
            && await db.Customers.AnyAsync(customer => customer.Id == coreOnlyCustomerId);
    }), "TRUNCATE retains protected shared customers, other-source aliases and unrelated Core-only profiles without false applied acknowledgement");
    Check(await Scoped(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        return await db.Orders.IgnoreQueryFilters().AnyAsync(order => order.Id == nativeOrder.CoreId && order.IsDeleted)
            && await db.TankTransfers.IgnoreQueryFilters().AnyAsync(transfer => transfer.Id == projectedTransfer.CoreId && transfer.IsDeleted);
    }), "TRUNCATE discovers orders/transfers through exact source-installation native bindings without metadata");
    Check(await Scoped(services => services.GetRequiredService<ApplicationDbContext>().Orders.AnyAsync(order => order.Id == otherInstallationOrder.CoreId)),
        "TRUNCATE never adopts another installation's source-owned order bindings");
    Check(await Scoped(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        return await db.TankEngineHistories.IgnoreQueryFilters().AnyAsync(history => history.Id == projectedHistory.CoreId && history.IsDeleted)
            && await db.TankEngineHistories.AnyAsync(history => history.Id == coreOnlyHistoryId && history.LegacyId == null);
    }), "History TRUNCATE finds explicit NEWAPP LegacyIds across the crash gap and preserves Core-only NULL LegacyIds");
    Check(truncating.EventStates.GetValueOrDefault("12") == "applied" && truncating.EventStates.GetValueOrDefault("13") == "applied"
        && truncating.EventStates.GetValueOrDefault("14") == "applied" && await Scoped(async services =>
        {
            var metadata = new NativeProjectionStore(services.GetRequiredService<ApplicationDbContext>());
            return (await metadata.FindAsync("newapp", install, truncating.Generation, "customer", gapCustomerId, default))?.State == "applied"
                && (await metadata.FindAsync("newapp", install, truncating.Generation, "order", orderId, default))?.State == "applied"
                && (await metadata.FindAsync("newapp", install, truncating.Generation, "transfer", moveId, default))?.State == "applied"
                && (await metadata.FindAsync("newapp", install, truncating.Generation, "tank-engine-history", eventHistoryId, default))?.State == "applied";
        }), "Crash-gap TRUNCATE commits native tombstones and durable per-identity results before acknowledging its supported events");
}
Console.WriteLine($"PASS {checks} source native projection checks.");

public sealed class FixedOptions<T>(T value) : IOptionsMonitor<T> where T : class
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
public class PermissionStub : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name == nameof(IPermissionService.InvalidateCacheAsync)
        ? Task.CompletedTask : throw new NotSupportedException("Permission reads are outside synthetic projection tests.");
}

public sealed class BackfillFixtureCrashException : Exception { }

public sealed class BackfillBusinessStub : ISourceBusinessProjector
{
    public Task<BusinessProjectionResult> ProjectBaselineAsync(string source, Guid installationId, Guid generation, CancellationToken ct) => Task.FromResult(new BusinessProjectionResult("applied"));
    public Task<BusinessProjectionResult> ProjectEventAsync(SourceSyncPendingEvent ev, CancellationToken ct) => Task.FromResult(new BusinessProjectionResult("unsupported"));
}

public sealed class BackfillRawStub(List<SourceSyncRawRow> history) : ISourceSyncRawReader
{
    public Guid Installation { get; set; } = Guid.NewGuid();
    public Guid Generation { get; } = Guid.NewGuid();
    public List<SourceSyncRawRow> History { get; } = history;
    public List<SourceSyncPendingEvent> Pending { get; } = [];
    public Dictionary<string, string> EventStates { get; } = [];
    public List<string[]> SnapshotRequests { get; } = [];
    public string LastBaselineState { get; private set; } = "pending";
    public int PageCalls { get; private set; }
    public int PageSizeOverride { get; set; } = 200;
    public int? FailOnPageCall { get; set; }
    public int DelayNextPageMilliseconds { get; set; }
    public SourceSyncRawRow? Customer { get; set; }
    public List<SourceSyncTable> AdditionalTables { get; } = [];
    public static string Key(SourceSyncRawRow row) => Convert.ToHexString(SourceSyncCodec.Hash(new[] { row.Values["Id"] }));
    public List<SourceSyncRawRow> OrderedHistory() => History.OrderBy(Key, StringComparer.Ordinal).ToList();
    public Task<SourceSyncState?> GetStateAsync(string source, CancellationToken ct) => Task.FromResult<SourceSyncState?>(new(source, Installation, Generation, "ready", 2, Pending.Count, LastBaselineState));
    public Task<IReadOnlyList<SourceSyncTable>> GetTablesAsync(string source, CancellationToken ct)
    {
        List<SourceSyncTable> tables = [new("Tank", ["Id", "RFID"], ["Id"], "fixture-tank"),
            new("TankEngineHistory", ["Id", "EngineId", "RFID", "CreateTime", "Type"], ["Id"], "fixture-history")];
        if (Customer is not null) tables.Add(new("Customer", Customer.Values.Keys.ToArray(), ["Id"], "fixture-customer"));
        tables.AddRange(AdditionalTables);
        return Task.FromResult<IReadOnlyList<SourceSyncTable>>(tables);
    }
    public async IAsyncEnumerable<SourceSyncRawRow> ReadRowsAsync(string source, string table, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    { yield return await Task.FromException<SourceSyncRawRow>(new InvalidOperationException("History backfill must use bounded pages.")); }
    public Task<IReadOnlyDictionary<string, IReadOnlyList<SourceSyncRawRow>>> ReadProjectionSnapshotAsync(string source, string[] tables, CancellationToken ct)
    {
        SnapshotRequests.Add(tables);
        return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<SourceSyncRawRow>>>(tables.ToDictionary(table => table,
            table => (IReadOnlyList<SourceSyncRawRow>)(table == "Customer" && Customer is not null ? [Customer] : Array.Empty<SourceSyncRawRow>()), StringComparer.Ordinal));
    }
    public Task<SourceSyncRawRow?> ReadRowAsync(string source, string table, IReadOnlyDictionary<string, string?> key, CancellationToken ct) =>
        Task.FromResult(History.SingleOrDefault(row => row.Values["Id"] == key["Id"]));
    public async Task<SourceSyncRawPage> ReadPageAsync(string source, string table, string? after, int limit,
        IReadOnlyDictionary<string, string?>? filters, string? search, string[]? columns, CancellationToken ct)
    {
        if (++PageCalls == FailOnPageCall) throw new BackfillFixtureCrashException();
        var delay = DelayNextPageMilliseconds; DelayNextPageMilliseconds = 0;
        if (delay > 0) await Task.Delay(delay, ct);
        var afterHex = after is null ? null : Convert.ToHexString(Convert.FromBase64String(after));
        var remaining = OrderedHistory().Where(row => afterHex is null || StringComparer.Ordinal.Compare(Key(row), afterHex) > 0).ToArray();
        var rows = remaining.Take(Math.Min(limit, PageSizeOverride)).ToArray();
        return new(rows, remaining.Length > rows.Length ? Convert.ToBase64String(Convert.FromHexString(Key(rows[^1]))) : null);
    }
    public Task<IReadOnlyList<SourceSyncPendingEvent>> ReadPendingAsync(string source, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SourceSyncPendingEvent>>(Pending.Where(ev => EventStates.GetValueOrDefault(ev.EventId) != "applied").Take(limit).ToArray());
    public Task SetProjectionStateAsync(string source, Guid installation, string eventId, string state, string? error, CancellationToken ct)
    { EventStates[eventId] = state; return Task.CompletedTask; }
    public Task SetBaselineProjectionStateAsync(string source, Guid generation, string state, string? error, CancellationToken ct)
    { LastBaselineState = state; return Task.CompletedTask; }
}
