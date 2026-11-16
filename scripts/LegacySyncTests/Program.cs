using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CorePlasma.API.Controllers;
using CorePlasma.API.Extensions;
using CorePlasma.BAL.DTOs.Auth;
using CorePlasma.BAL.DTOs.Integration;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Extensions;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Extensions;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Models;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Entity = CorePlasma.DAL.Entities;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var checks = 0;
void Check(bool result, string label) { if (!result) throw new InvalidOperationException(label); checks++; Console.WriteLine("PASS: " + label); }
var baseline = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION") ?? "Host=127.0.0.1;Port=5432;Username=kienkien;Database=postgres";
var options = new NpgsqlConnectionStringBuilder(baseline) { Pooling = false, Timeout = 10, CommandTimeout = 30 };
if (!(options.Host is "127.0.0.1" or "localhost" or "::1" || (options.Host ?? "").StartsWith("/private/tmp/", StringComparison.Ordinal)))
    throw new InvalidOperationException("Only a local disposable PostgreSQL server is allowed.");
options.Database = "postgres";
var adminConnection = options.ConnectionString;
var database = "core_legacy_tests_" + Guid.NewGuid().ToString("N");
await using (var admin = new NpgsqlConnection(adminConnection))
{
    await admin.OpenAsync();
    await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
    await command.ExecuteNonQueryAsync();
}
options.Database = database;
var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production, ApplicationName = typeof(IntegrationLegacyController).Assembly.GetName().Name });
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ConnectionStrings:DefaultConnection"] = options.ConnectionString, ["ApiKey:Key"] = key,
    ["Jwt:Secret"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), ["Jwt:Issuer"] = "legacy-tests", ["Jwt:Audience"] = "legacy-tests"
});
var policy = new TestLegacyPolicy();
policy.Value.ProjectionOwners = new() { ["tank"] = "plasma-bv", ["engine"] = "plasma-bv", ["user"] = "plasma-bv" };
builder.Logging.ClearProviders();
builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
builder.Services.AddApplicationServices(builder.Configuration).AddBusinessServices().AddDalServices(builder.Configuration);
builder.Services.AddSingleton<IOptionsMonitor<LegacySyncSettings>>(policy);
builder.Services.AddControllers().ConfigureApplicationPartManager(parts =>
{ parts.ApplicationParts.Clear(); parts.ApplicationParts.Add(new AssemblyPart(typeof(IntegrationLegacyController).Assembly)); });
await using var app = builder.Build();
app.UseExceptionHandler(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
var canonicalId = Guid.NewGuid(); var singleId = Guid.NewGuid(); var tankId = Guid.NewGuid();
var newAppCustomerId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var accountId = Guid.NewGuid();
async Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> operation)
{
    await using var scope = app.Services.CreateAsyncScope();
    return await operation(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
}
JsonElement Customer(long id, string name = "Source Clinic", string status = "active", string? deleted = null, string phone = "0900000000") =>
    JsonSerializer.SerializeToElement(new { id, name, address = "Synthetic source address", phone_number = phone, status, old_id = "", deleted_at = deleted,
        _reseller_user_ids = new[] { 42 }, _customer_user_ids = new[] { 43 } });
JsonElement Tank(long id = 10, long? customer = 1, string status = "occupied") => JsonSerializer.SerializeToElement(new
{
    id, serial = "KC0001", old_id = "", status, customer_id = customer, warehouse_id = 7,
    _capacity = new { id = 1, name = "Synthetic 10L", value = 10 }, _warehouse = new { id = 7, name = "Stale Warehouse" },
    _factory = (object?)null
});
LegacyEventIngestDto Event(long sequence, long id, JsonElement data, string entity = "customer", string operation = "update") => new()
{ EventId = Guid.NewGuid(), Source = "plasma-bv", Sequence = sequence, Entity = entity, EntityId = id, Operation = operation, OccurredAt = DateTimeOffset.UtcNow, Data = data };
HttpClient? http = null;
async Task<LegacyEventIngestResultDto?> Send(LegacyEventIngestDto dto, HttpStatusCode expected, string? credential = "valid")
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "api/integration/legacy/events")
    { Content = new StringContent(JsonSerializer.Serialize(dto), Encoding.UTF8, "application/json") };
    if (credential is not null) request.Headers.TryAddWithoutValidation("X-API-Key", credential == "valid" ? key : credential);
    using var response = await http!.SendAsync(request);
    Check(response.StatusCode == expected, $"HTTP receiver expected {(int)expected}, got {(int)response.StatusCode}");
    return expected == HttpStatusCode.OK ? JsonSerializer.Deserialize<LegacyEventIngestResultDto>(await response.Content.ReadAsStringAsync()) : null;
}
try
{
    await Db(async context => { await context.GetService<IMigrator>().MigrateAsync("20261012050111_AddHistoryIngestion"); return true; });
    await Db(async context =>
    {
        // Seed the pre-upgrade schema explicitly; the current EF model already contains the new columns.
        foreach (var native in new[] { (canonicalId,"CURATED-CANONICAL","Curated Native Name","Active"),
            (singleId,"PLASMA-C3","Single","Suspended"),(newAppCustomerId,"NEWAPP-C600","Existing NEWAPP","Active") })
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Customers" ("Id","CustomerCode","CustomerName","Phone","Address","Status","CreatedDate","IsDeleted")
                VALUES ({native.Item1},{native.Item2},{native.Item3},'0900000000','Synthetic source address',{native.Item4},now() AT TIME ZONE 'utc',false)
                """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Tanks" ("Id","TankCode","SerialNumber","LegacyId","CustomerId","Status","Manufacturer","CreatedDate","IsDeleted")
            VALUES ({tankId},'KC1','KC1','plasma:10',{canonicalId},'AtClient','Preserve Maker',now() AT TIME ZONE 'utc',false);
            INSERT INTO "Employees" ("Id","EmployeeCode","FullName","Gender","Status","CreatedDate","IsDeleted")
            VALUES ({employeeId},'OLD-EMP700','Existing employee profile','Unknown','Active',now() AT TIME ZONE 'utc',false);
            INSERT INTO "EmployeeUsers" ("Id","EmployeeId","Username","PasswordHash","Status","IsSuperAdmin","AccessFailedCount","CreatedDate","IsDeleted")
            VALUES ({accountId},{employeeId},'synthetic-account','KEEP-CREDENTIAL','Active',false,0,now() AT TIME ZONE 'utc',false)
            """);
        await context.Database.ExecuteSqlRawAsync("CREATE SCHEMA newapp_migration; CREATE TABLE newapp_migration.customer_map(source_id bigint PRIMARY KEY,target_id uuid NOT NULL,source_data jsonb); CREATE TABLE newapp_migration.user_map(source_id bigint PRIMARY KEY,target_id uuid,employee_id uuid,kind text,state text,source_data jsonb);");
        var newAppCustomerData = JsonSerializer.Serialize(new { Id = 600, Name = "Existing NEWAPP", Address = "Synthetic source address", Tel = "0900000000", IsDeleted = false });
        var newAppUserData = JsonSerializer.Serialize(new { Id = 700, Name = "Existing employee profile", Email = "synthetic@example.invalid", PhoneNumber = "0900000000", IsActive = true, UserType = 1, IsDeleted = false, PasswordHash = "DO-NOT-COPY", PasswordSalt = "DO-NOT-COPY" });
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO newapp_migration.customer_map VALUES (600,{newAppCustomerId},{newAppCustomerData}::jsonb)");
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO newapp_migration.user_map VALUES (700,{accountId},{employeeId},'employee','mapped_existing',{newAppUserData}::jsonb)");
        await context.Database.ExecuteSqlRawAsync("CREATE SCHEMA plasma_migration; CREATE TABLE plasma_migration.customer_map(source_id integer PRIMARY KEY,target_id uuid,source_data jsonb NOT NULL,method text NOT NULL,synced_at timestamp NOT NULL); CREATE TABLE plasma_migration.tank_map(source_id integer PRIMARY KEY,target_id uuid UNIQUE,source_data jsonb NOT NULL,method text NOT NULL,synced_at timestamp NOT NULL);");
        foreach (var pair in new[] { (1L, (Guid?)canonicalId), (2L, (Guid?)canonicalId), (3L, (Guid?)singleId), (4L, (Guid?)null) })
        {
            var data = Customer(pair.Item1, deleted: pair.Item1 == 4 ? "2026-10-01T00:00:00Z" : null).GetRawText();
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO plasma_migration.customer_map VALUES ({pair.Item1},{pair.Item2},{data}::jsonb,'synthetic',now() AT TIME ZONE 'utc')");
        }
        var tankData = Tank().GetRawText();
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO plasma_migration.tank_map VALUES (10,{tankId},{tankData}::jsonb,'synthetic',now() AT TIME ZONE 'utc')");
        await context.Database.MigrateAsync();
        return true;
    });
    Check(await Db(c => c.LegacySyncMappings.CountAsync()) == 7, "Migration bootstraps authoritative historical identities and source-deleted null targets");
    Check(await Db(c => c.LegacySyncMappings.CountAsync(x => x.PreserveCanonical)) == 2, "Many-to-one customer mappings preserve canonical survivor ownership");
    Check(await Db(c => c.Customers.AnyAsync(x => x.Id == canonicalId && x.QuanLyId == 1 && x.NewAppId == null)) &&
        await Db(c => c.Customers.AnyAsync(x => x.Id == newAppCustomerId && x.NewAppId == 600 && x.QuanLyId == null)),
        "Migration adds representative source-system IDs without replacing native GUIDs or merged aliases");
    Check(await Db(c => c.Employees.AnyAsync(x => x.Id == employeeId && x.NewAppId == 700)) &&
        await Db(c => c.LegacySyncMappings.AnyAsync(x => x.Source == "newapp" && x.Entity == "user" && x.CoreId == employeeId)),
        "NEWAPP user bootstrap uses employee profile GUID rather than login account GUID");
    Check(!(await Db(c => c.LegacySyncMappings.Where(x => x.Entity == "user").Select(x => x.SnapshotJson).SingleAsync())).Contains("Password",StringComparison.OrdinalIgnoreCase),
        "Bootstrap copies only public user profile fields and excludes source credentials");
    await app.StartAsync();
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(20) };
    var initial = Event(1, 1, Customer(1));
    await Send(initial, HttpStatusCode.Unauthorized, null);
    await Send(initial, HttpStatusCode.Unauthorized, "wrong");
    var ack = await Send(initial, HttpStatusCode.OK);
    Check(ack!.EventId == initial.EventId && ack.Sequence == 1 && ack.Source == "plasma-bv" && ack.Status == "applied", "Raw acknowledgement matches immutable event identity");
    Check(await Db(c => c.Customers.Where(x => x.Id == canonicalId).Select(x => x.CustomerName).SingleAsync()) == "Curated Native Name", "Unchanged aliases do not reset curated native fields");
    Check((await Send(initial, HttpStatusCode.OK))!.Status == "duplicate", "Lost acknowledgement can retry without duplicating mutation or receipt");
    initial.SourceSystem = "quanly.plasma.vn";
    Check((await Send(initial, HttpStatusCode.OK))!.Status == "duplicate", "Upgraded sender retries a pre-domain receipt without invalidating its original content hash");
    var conflict = Event(1, 1, Customer(1));
    await Send(conflict, HttpStatusCode.Conflict);
    initial.Data = Customer(1, "Changed content");
    await Send(initial, HttpStatusCode.Conflict);
    await Send(Event(3, 1, Customer(1, "Conflicting alias name")), HttpStatusCode.OK);
    Check(await Db(c => c.Customers.Where(x => x.Id == canonicalId).Select(x => x.CustomerName).SingleAsync()) == "Curated Native Name", "A conflicting alias name cannot overwrite canonical native customer");
    await Send(Event(4, 2, Customer(2, "Conflicting alias name")), HttpStatusCode.OK);
    Check(await Db(c => c.Customers.Where(x => x.Id == canonicalId).Select(x => x.CustomerName).SingleAsync()) == "Conflicting alias name", "Matching changed aliases propagate one agreed canonical value");
    Check((await Send(Event(2, 1, Customer(1, "Old value")), HttpStatusCode.OK))!.Status == "stale", "Late source revision is receipted without overwriting newer state");
    await Send(Event(5, 1, Customer(1, deleted: "2026-10-27T00:00:00Z"), operation: "delete"), HttpStatusCode.OK);
    Check(await Db(c => c.Customers.AnyAsync(x => x.Id == canonicalId)), "Deleting a merged source alias never deletes its canonical survivor");
    await Send(Event(6, 2, Customer(2, "Conflicting alias name", "inactive")), HttpStatusCode.OK);
    Check(await Db(c => c.Customers.Where(x => x.Id == canonicalId).Select(x => x.Status).SingleAsync()) == CustomerStatus.Active, "An inactive merged alias does not downgrade canonical Active status");
    await Send(Event(7, 3, Customer(3, "Changed Single")), HttpStatusCode.OK);
    Check(await Db(c => c.Customers.Where(x => x.Id == singleId).Select(x => x.Status).SingleAsync()) == CustomerStatus.Suspended, "Source updates preserve native Suspended status");
    await Send(Event(8, 10, Tank(customer: 2), "tank"), HttpStatusCode.OK);
    var stored = await Db(c => c.Tanks.SingleAsync(x => x.Id == tankId));
    Check(stored.CurrentLocation == "Conflicting alias name" && stored.Capacity == 10 && stored.Manufacturer == "Preserve Maker" && stored.LegacyId == "plasma:10" && stored.SerialNumber == "KC1", "Tank updates reuse authoritative identity and capacity while preserving curated manufacturer, legacy ID and equivalent serial");
    Check(await Db(c => c.LegacySyncMappings.Where(x => x.Entity == "customer" && x.EntityId == 2).Select(x => x.SnapshotJson).SingleAsync()) is { } audit && audit.Contains("_reseller_user_ids"), "Full source snapshots retain relationships not represented natively");
    var receiptCount = await Db(c => c.LegacySyncReceipts.CountAsync());
    await Send(Event(9, 10, Tank(customer: 999), "tank"), HttpStatusCode.Conflict);
    Check(await Db(c => c.LegacySyncReceipts.CountAsync()) == receiptCount, "Failed native mapping rolls back receipt and source snapshot changes");
    await Send(Event(10, 99, Customer(99)), HttpStatusCode.Conflict);
    await Send(Event(11, 99, Customer(99, "New Customer"), operation: "create"), HttpStatusCode.OK);
    Check(await Db(c => c.Customers.AnyAsync(x => x.CustomerCode == "PLASMA-C99")), "New source create produces one native customer with stable source code");
    await Send(Event(12, 4, Customer(4, "Restored Never Imported")), HttpStatusCode.OK);
    Check(await Db(c => c.LegacySyncMappings.AnyAsync(x => x.Entity == "customer" && x.EntityId == 4 && x.CoreId != null)), "Restoring a never-imported source tombstone persists its newly allocated native ID");
    await Send(Event(13, 11, JsonSerializer.SerializeToElement(new { id = 11, serial = "NEW11", old_id = "", status = "ready", customer_id = (long?)null, _capacity = new { id = 1, value = 5, name = "5L" }, _warehouse = (object?)null, _factory = (object?)null }), "tank", "create"), HttpStatusCode.OK);
    Check(await Db(c => c.Tanks.AnyAsync(x => x.TankCode == "NEW11" && x.Status == TankStatus.InStock && x.Capacity == 5)), "New native tank requires resolved capacity metadata and stable physical code");
    await Send(Event(14, 11, JsonSerializer.SerializeToElement(new { id = 11, serial = "NEW11", old_id = "" }), "tank", "delete"), HttpStatusCode.OK);
    await Send(Event(15, 11, JsonSerializer.SerializeToElement(new { id = 11, serial = "NEW11", old_id = "", status = "shipping", customer_id = (long?)null }), "tank"), HttpStatusCode.Conflict);
    Check(await Db(c => c.Tanks.IgnoreQueryFilters().AnyAsync(x => x.TankCode == "NEW11" && x.IsDeleted && x.Status == TankStatus.Deleted)), "Unsupported restore keeps native tank tombstone intact");
    await Send(Event(16, 10, JsonSerializer.SerializeToElement(new { id = 10, serial = "KC0001", old_id = "", status = "ready", customer_id = (long?)null,
        _capacity = new { id = 1, value = 10, name = "10L" }, _warehouse = (object?)null, _factory = (object?)null }), "tank"), HttpStatusCode.OK);
    Check(await Db(c => c.Tanks.AnyAsync(x => x.Id == tankId && x.CustomerId == null && x.CurrentLocation == null)), "Clearing all source location references clears native location and customer assignment");
    await Task.WhenAll(Send(Event(17, 99, Customer(99, "Older concurrent")), HttpStatusCode.OK), Send(Event(18, 99, Customer(99, "Latest concurrent")), HttpStatusCode.OK));
    Check(await Db(c => c.Customers.AnyAsync(x => x.CustomerCode == "PLASMA-C99" && x.CustomerName == "Latest concurrent")), "Concurrent out-of-order requests preserve the highest source sequence");
    await Send(Event(19, 1, JsonSerializer.SerializeToElement(new { id = "wrong-type" })), HttpStatusCode.BadRequest);
    await Db(async c => { var row = await c.Customers.SingleAsync(x => x.CustomerCode == "PLASMA-C99"); row.IsDeleted = true; row.Status = CustomerStatus.Deleted; await c.SaveChangesAsync(); return true; });
    await Send(Event(20, 99, Customer(99, "Do not revive")), HttpStatusCode.Conflict);
    var nativeDeletedCustomer = Event(21, 99, Customer(99, deleted: "2026-10-27T00:00:00Z"), operation: "delete");
    await Send(nativeDeletedCustomer, HttpStatusCode.OK);
    Check((await Send(nativeDeletedCustomer, HttpStatusCode.OK))!.Status == "duplicate", "A source delete acknowledges an already native-deleted customer without reviving it");
    await Send(Event(22, 12, JsonSerializer.SerializeToElement(new { id = 12, serial = "NEW12", old_id = "", status = "ready", customer_id = (long?)null,
        _capacity = new { id = 1, value = 5, name = "5L" }, _warehouse = (object?)null, _factory = (object?)null }), "tank", "create"), HttpStatusCode.OK);
    await Db(async c => { var row = await c.Tanks.SingleAsync(x => x.TankCode == "NEW12"); row.Status = TankStatus.InUse; await c.SaveChangesAsync(); return true; });
    var inUseDelete = Event(23, 12, JsonSerializer.SerializeToElement(new { id = 12, serial = "NEW12", old_id = "" }), "tank", "delete");
    var beforeDelete = await Db(c => c.LegacySyncReceipts.CountAsync());
    await Send(inUseDelete, HttpStatusCode.Conflict);
    Check(await Db(c => c.LegacySyncReceipts.CountAsync()) == beforeDelete && await Db(c => c.Tanks.AnyAsync(x => x.TankCode == "NEW12" && x.Status == TankStatus.InUse)), "In-use native tank deletion is rejected atomically without receipt");
    await Db(async c => { var row = await c.Tanks.SingleAsync(x => x.TankCode == "NEW12"); row.Status = TankStatus.Available; await c.SaveChangesAsync(); return true; });
    await Send(inUseDelete, HttpStatusCode.OK);
    Check((await Send(inUseDelete, HttpStatusCode.OK))!.Status == "duplicate", "A retained delete event succeeds safely after native use ends");
    await Db(async c => { var row = await c.Tanks.SingleAsync(x => x.Id == tankId); row.IsDeleted = true; row.Status = TankStatus.Deleted; await c.SaveChangesAsync(); return true; });
    await Send(Event(24, 10, Tank(customer: 2), "tank"), HttpStatusCode.Conflict);
    var nativeDeletedTank = Event(25, 10, JsonSerializer.SerializeToElement(new { id = 10, serial = "KC0001", old_id = "" }), "tank", "delete");
    await Send(nativeDeletedTank, HttpStatusCode.OK);
    Check((await Send(nativeDeletedTank, HttpStatusCode.OK))!.Status == "duplicate", "A source delete acknowledges an already native-deleted tank while source updates cannot revive it");
    var tanksBefore = await Db(c => c.Tanks.IgnoreQueryFilters().CountAsync());
    await Send(Event(26, 30, JsonSerializer.SerializeToElement(new { id = 30, serial = "kc0001", old_id = "", status = "ready", customer_id = (long?)null,
        _capacity = new { id = 1, value = 5, name = "5L" }, _warehouse = (object?)null, _factory = (object?)null }), "tank", "create"), HttpStatusCode.Conflict);
    Check(await Db(c => c.Tanks.IgnoreQueryFilters().CountAsync()) == tanksBefore, "Unmapped zero-padded/case variant cannot duplicate another source-owned physical tank");
    var unmatched = Guid.NewGuid();
    await Db(async c => { c.Tanks.Add(new Entity.Tank { Id = unmatched, TankCode = "ZZ12", SerialNumber = "ZZ12" }); await c.SaveChangesAsync(); return true; });
    var unverifiedTank = Event(27, 31, JsonSerializer.SerializeToElement(new { id = 31, serial = "zz0012", old_id = "", status = "ready", customer_id = (long?)null,
        _capacity = new { id = 1, value = 5, name = "5L" }, _warehouse = (object?)null, _factory = (object?)null }), "tank", "create");
    await Send(unverifiedTank, HttpStatusCode.Conflict);
    Check(!await Db(c => c.LegacySyncMappings.AnyAsync(x => x.Entity == "tank" && x.EntityId == 31)) &&
        await Db(c => c.Tanks.AnyAsync(x => x.Id == unmatched && x.QuanLyId == null)), "Matching physical serial cannot update an existing tank without a verified source ID");
    await using (var scope = app.Services.CreateAsyncScope())
        await scope.ServiceProvider.GetRequiredService<ILegacyEventIngestService>().BindMappingAsync(new LegacyMappingBindDto
        { Source = "plasma-bv", SourceSystem = "quanly.plasma.vn", Entity = "tank", EntityId = 31, CoreId = unmatched }, default);
    await Send(unverifiedTank, HttpStatusCode.OK);
    Check(await Db(c => c.Tanks.AnyAsync(x => x.Id == unmatched && x.QuanLyId == 31)), "A verified administrative tank mapping permits a retained event to retry safely");
    // Cross-system numeric IDs, engine and user identity checks use synthetic records only.
    LegacyEventIngestDto SystemEvent(string source, string system, string entity, long id, long sequence, JsonElement data, string operation = "create") => new()
    { EventId = Guid.NewGuid(), Source = source, SourceSystem = system, Entity = entity, EntityId = id, Sequence = sequence, Data = data, Operation = operation, OccurredAt = DateTimeOffset.UtcNow };
    JsonElement Engine(long id, string serial = "ENGINE900", string status = "active") =>
        JsonSerializer.SerializeToElement(new { id, name = "Synthetic engine", serial, version = "1.0", old_id = "", status });
    JsonElement User(long id, string name = "Synthetic staff", bool active = true) =>
        JsonSerializer.SerializeToElement(new { id, name, email = "synthetic@example.invalid", phone_number = "0900000000", is_active = active, deleted_at = (string?)null });
    await Send(SystemEvent("plasma-bv","quanly.plasma.vn","customer",900,30,Customer(900,"Quanly ID 900")),HttpStatusCode.OK);
    await Send(SystemEvent("newapp","newapp.plasmamed.vn","customer",900,1,Customer(900,"NEWAPP ID 900")),HttpStatusCode.OK);
    var quanly900 = await Db(c => c.Customers.SingleAsync(x => x.QuanLyId == 900));
    var newapp900 = await Db(c => c.Customers.SingleAsync(x => x.NewAppId == 900));
    Check(quanly900.Id != newapp900.Id && quanly900.NewAppId == null && newapp900.QuanLyId == null,
        "The same numeric ID from two source systems remains two distinct verified identities");
    var wrongDomain = SystemEvent("plasma-bv","newapp.plasmamed.vn","customer",900,31,Customer(900));
    await Send(wrongDomain,HttpStatusCode.BadRequest);
    var missingDomain = SystemEvent("newapp","newapp.plasmamed.vn","customer",900,2,Customer(900)); missingDomain.SourceSystem = null;
    await Send(missingDomain,HttpStatusCode.BadRequest);
    var wrongCore = SystemEvent("plasma-bv","quanly.plasma.vn","customer",900,31,Customer(900,"Wrong Core"),"update"); wrongCore.CoreId = newapp900.Id;
    await Send(wrongCore,HttpStatusCode.Conflict);
    Check(await Db(c => c.Customers.AnyAsync(x => x.Id == quanly900.Id && x.CustomerName == "Quanly ID 900")),
        "Mismatched optional Core GUID cannot overwrite the mapped source customer");
    var engineEvent = SystemEvent("plasma-bv","quanly.plasma.vn","engine",900,32,Engine(900));
    await Send(engineEvent,HttpStatusCode.OK);
    var engine900 = await Db(c => c.Engines.SingleAsync(x => x.QuanLyId == 900));
    Check(engine900.Id != Guid.Empty && engine900.NewAppId == null && engine900.EngineName == "Synthetic engine", "New engine keeps a native GUID and its source-system ID");
    await Send(SystemEvent("newapp","newapp.plasmamed.vn","engine",900,2,Engine(900,"ENGINE900")),HttpStatusCode.Conflict);
    await Send(SystemEvent("plasma-bv","quanly.plasma.vn","engine",901,33,Engine(901,"ENGINE901"),"update"),HttpStatusCode.Conflict);
    var wrongEngineCore = SystemEvent("plasma-bv","quanly.plasma.vn","engine",900,34,Engine(900,"ENGINE900","repair"),"update"); wrongEngineCore.CoreId = Guid.NewGuid();
    await Send(wrongEngineCore,HttpStatusCode.Conflict);
    var engineUpdate = SystemEvent("plasma-bv","quanly.plasma.vn","engine",900,34,Engine(900,"ENGINE900","checking"),"update"); engineUpdate.CoreId = engine900.Id;
    await Send(engineUpdate,HttpStatusCode.OK);
    Check(await Db(c => c.Engines.AnyAsync(x => x.Id == engine900.Id && x.Status == EngineStatus.Checking)), "A verified engine update targets its existing native GUID");
    await Send(SystemEvent("plasma-bv","quanly.plasma.vn","user",900,35,User(900)),HttpStatusCode.OK);
    await Send(SystemEvent("newapp","newapp.plasmamed.vn","user",900,3,User(900,"NEWAPP employee")),HttpStatusCode.OK);
    var staff900 = await Db(c => c.Employees.SingleAsync(x => x.QuanLyId == 900));
    Check(staff900.NewAppId == null && await Db(c => c.Employees.AnyAsync(x => x.NewAppId == 900 && x.Id != staff900.Id)),
        "Staff profiles keep separate IDs across the two source systems");
    Check(await Db(c => c.EmployeeUsers.CountAsync()) == 1 && await Db(c => c.EmployeeUsers.AnyAsync(x => x.Id == accountId && x.PasswordHash == "KEEP-CREDENTIAL")),
        "Source user profile synchronization cannot provision or alter Core login accounts");
    await Send(SystemEvent("plasma-bv","quanly.plasma.vn","user",901,36,User(901),"update"),HttpStatusCode.Conflict);
    await Send(SystemEvent("plasma-bv","quanly.plasma.vn","user",900,36,JsonSerializer.SerializeToElement(new { id=900,name="Blocked",email="x",phone_number="x",is_active=true,password_hash="DO-NOT-STORE" }),"update"),HttpStatusCode.BadRequest);
    await Send(SystemEvent("newapp","newapp.plasmamed.vn","user",902,4,JsonSerializer.SerializeToElement(new { id=902,name="Device",email="x",phone_number="x",is_active=true,user_type=6 })),HttpStatusCode.BadRequest);
    var mismatchedUser = SystemEvent("plasma-bv","quanly.plasma.vn","user",900,36,User(901),"update");
    await Send(mismatchedUser,HttpStatusCode.BadRequest);
    // Bind an alias from the second system and verify a delete cannot retire its active peer.
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var service = scope.ServiceProvider.GetRequiredService<ILegacyEventIngestService>();
        await service.BindMappingAsync(new LegacyMappingBindDto { Source="newapp",SourceSystem="newapp.plasmamed.vn",Entity="tank",EntityId=31,CoreId=unmatched },default);
        await service.BindMappingAsync(new LegacyMappingBindDto { Source="newapp",SourceSystem="newapp.plasmamed.vn",Entity="customer",EntityId=601,CoreId=canonicalId },default);
        var identities = await service.GetIdentitiesAsync("customer",canonicalId,default);
        Check(identities.Count == 3 && identities.Any(x => x.SourceSystem == "quanly.plasma.vn" && x.EntityId == 1) &&
            identities.Any(x => x.SourceSystem == "newapp.plasmamed.vn" && x.EntityId == 601), "Read-only identity metadata preserves every merged alias and source system");
    }
    await Send(SystemEvent("newapp","newapp.plasmamed.vn","tank",31,4,JsonSerializer.SerializeToElement(new {id=31}),"delete"),HttpStatusCode.OK);
    Check(await Db(c => c.Tanks.AnyAsync(x => x.Id == unmatched && x.QuanLyId == 31 && x.NewAppId == 31)), "Deleting one cross-system tank alias preserves the native tank while its other identity remains active");
    await Send(SystemEvent("newapp","newapp.plasmamed.vn","customer",601,5,Customer(601,deleted:"2026-10-27T00:00:00Z"),"delete"),HttpStatusCode.OK);
    Check(await Db(c => c.Customers.AnyAsync(x => x.Id == canonicalId)), "Deleting one cross-system customer alias preserves the canonical native customer");
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var service = scope.ServiceProvider.GetRequiredService<ILegacyEventIngestService>();
        await service.BindMappingAsync(new LegacyMappingBindDto { Source="newapp",SourceSystem="newapp.plasmamed.vn",Entity="engine",EntityId=900,CoreId=engine900.Id },default);
        await service.BindMappingAsync(new LegacyMappingBindDto { Source="newapp",SourceSystem="newapp.plasmamed.vn",Entity="user",EntityId=901,CoreId=staff900.Id },default);
    }
    await Send(SystemEvent("newapp","newapp.plasmamed.vn","engine",900,6,JsonSerializer.SerializeToElement(new {id=900}),"delete"),HttpStatusCode.OK);
    await Send(SystemEvent("newapp","newapp.plasmamed.vn","user",901,7,JsonSerializer.SerializeToElement(new {id=901}),"delete"),HttpStatusCode.OK);
    Check(await Db(c => c.Engines.AnyAsync(x => x.Id == engine900.Id && x.QuanLyId == 900 && x.NewAppId == 900)) &&
        await Db(c => c.Employees.AnyAsync(x => x.Id == staff900.Id && x.QuanLyId == 900 && x.NewAppId == 901)),
        "Deleting one cross-system engine or user alias preserves native records and both source IDs");
    var wrongUserCore = SystemEvent("plasma-bv","quanly.plasma.vn","user",900,36,User(900,"Wrong GUID"),"update"); wrongUserCore.CoreId = Guid.NewGuid();
    await Send(wrongUserCore,HttpStatusCode.Conflict);
    var wrongTankCore = Event(37,31,unverifiedTank.Data,"tank"); wrongTankCore.CoreId = Guid.NewGuid();
    await Send(wrongTankCore,HttpStatusCode.Conflict);
    var publicIdentitiesUrl = $"api/legacy-sync/mappings/customer/{canonicalId}";
    using (var request = new HttpRequestMessage(HttpMethod.Get,publicIdentitiesUrl))
    {
        request.Headers.TryAddWithoutValidation("X-API-Key",key);
        using var response = await http.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.Unauthorized,"Shared integration key cannot read administrative source identity metadata");
    }
    string token;
    await using (var scope = app.Services.CreateAsyncScope())
        token = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>().Generate(new TokenSubject(accountId,UserType.Employee,"synthetic-account","Synthetic",employeeId,null)).Token;
    using (var request = new HttpRequestMessage(HttpMethod.Get,publicIdentitiesUrl))
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
        using var response = await http.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.Forbidden,"Employee without module View permission cannot read source identity metadata");
    }
    await Db(async c => { var account=await c.EmployeeUsers.SingleAsync(x=>x.Id==accountId); account.IsSuperAdmin=true; await c.SaveChangesAsync(); return true; });
    await using (var scope = app.Services.CreateAsyncScope())
        await scope.ServiceProvider.GetRequiredService<IPermissionService>().InvalidateCacheAsync([employeeId],default);
    using (var request = new HttpRequestMessage(HttpMethod.Get,publicIdentitiesUrl))
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
        using var response = await http.SendAsync(request);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Check(response.StatusCode == HttpStatusCode.OK && payload.RootElement.GetProperty("data").GetArrayLength()==3,
            "Authorized source identity endpoint returns all aliases in the standard data envelope");
    }
    using (var request = new HttpRequestMessage(HttpMethod.Get,$"api/customers/{canonicalId}"))
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
        using var response = await http.SendAsync(request);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data=payload.RootElement.GetProperty("data");
        Check(response.StatusCode==HttpStatusCode.OK && data.GetProperty("quanLyId").GetInt64()==1 && data.GetProperty("newAppId").GetInt64()==601,
            "Ordinary detail DTO exposes independent native and source IDs for frontend administration");
    }
    await Send(SystemEvent("newapp","newapp.plasmamed.vn","user",700,8,User(700,"Do not deactivate admin",false),"update"),HttpStatusCode.Conflict);
    Check(await Db(c=>c.EmployeeUsers.AnyAsync(x=>x.Id==accountId && x.IsSuperAdmin && x.PasswordHash=="KEEP-CREDENTIAL")),
        "A source user update cannot deactivate a native Super Admin or alter its credentials");
    var beforeTransientCustomers=await Db(c=>c.Customers.IgnoreQueryFilters().CountAsync());
    var beforeTransientTanks=await Db(c=>c.Tanks.IgnoreQueryFilters().CountAsync());
    foreach (var entity in new[] { "customer","tank","engine","user" })
        await Send(SystemEvent("newapp","newapp.plasmamed.vn",entity,9999,20+Array.IndexOf(new[]{"customer","tank","engine","user"},entity),JsonSerializer.SerializeToElement(new{id=9999}),"delete"),HttpStatusCode.OK);
    Check(await Db(c=>c.LegacySyncMappings.CountAsync(x=>x.Source=="newapp" && x.EntityId==9999 && x.CoreId==null && x.IsDeleted))==4 &&
        await Db(c=>c.Customers.IgnoreQueryFilters().CountAsync())==beforeTransientCustomers && await Db(c=>c.Tanks.IgnoreQueryFilters().CountAsync())==beforeTransientTanks,
        "Unmapped source deletes retire all four identities as null-target tombstones without guessing or changing native records");
    var transientTank = JsonSerializer.SerializeToElement(new {id=9999,serial="TRANSIENT9999",old_id="",status="ready",customer_id=(long?)null,_capacity=new{id=1,name="5L",value=5},_warehouse=(object?)null,_factory=(object?)null});
    var restoredPayloads=new[]{("customer",Customer(9999,"Explicit new source customer")),("tank",transientTank),("engine",Engine(9999,"TRANSIENT-ENGINE9999")),("user",User(9999))};
    for (var index=0;index<restoredPayloads.Length;index++)
        await Send(SystemEvent("newapp","newapp.plasmamed.vn",restoredPayloads[index].Item1,9999,30+index,restoredPayloads[index].Item2,"update"),HttpStatusCode.Conflict);
    Check(await Db(c=>c.LegacySyncMappings.CountAsync(x=>x.Source=="newapp" && x.EntityId==9999 && x.CoreId==null && x.IsDeleted))==4,
        "An unknown delete followed by update cannot allocate a native record or bypass ID verification");
    for (var index=0;index<restoredPayloads.Length;index++)
        await Send(SystemEvent("newapp","newapp.plasmamed.vn",restoredPayloads[index].Item1,9999,40+index,restoredPayloads[index].Item2),HttpStatusCode.OK);
    Check(await Db(c=>c.LegacySyncMappings.CountAsync(x=>x.Source=="newapp" && x.EntityId==9999 && x.CoreId!=null && !x.IsDeleted))==4,
        "An explicit source create can replace a never-imported identity tombstone with a new verified native GUID");
    await Send(SystemEvent("newapp","newapp.plasmamed.vn","tank",8888,50,JsonSerializer.SerializeToElement(new{id=8888}),"delete"),HttpStatusCode.OK);
    await Send(SystemEvent("newapp","newapp.plasmamed.vn","tank",8888,51,JsonSerializer.SerializeToElement(new {id=8888,serial="ZZ0012",old_id="",status="ready",customer_id=(long?)null,_capacity=new{id=1,name="5L",value=5},_warehouse=(object?)null,_factory=(object?)null})),HttpStatusCode.Conflict);
    Check(await Db(c=>c.LegacySyncMappings.AnyAsync(x=>x.Source=="newapp" && x.Entity=="tank" && x.EntityId==8888 && x.CoreId==null)),
        "An explicit create after unknown-delete tombstone still cannot claim an existing physical tank without verified binding");
    var cachedAccountId=Guid.NewGuid(); var viewPermissionId=Guid.NewGuid();
    await Db(async c =>
    {
        c.EmployeeUsers.Add(new Entity.EmployeeUser {Id=cachedAccountId,EmployeeId=staff900.Id,Username="synthetic-cache-account",PasswordHash="PRESERVE-CACHE-CREDENTIAL",Status=UserStatus.Active});
        c.Permissions.Add(new Entity.Permission {Id=viewPermissionId,PermissionCode="CUSTOMER_VIEW",PermissionName="Synthetic view",ModuleCode="CUSTOMER",FunctionCode="CUSTOMER",ActionCode="VIEW",IsActive=true});
        c.EmployeePermissions.Add(new Entity.EmployeePermission {EmployeeId=staff900.Id,PermissionId=viewPermissionId,GrantedDate=DateTime.UtcNow,IsActive=true});
        await c.SaveChangesAsync(); return true;
    });
    await using(var scope=app.Services.CreateAsyncScope())
        Check(await scope.ServiceProvider.GetRequiredService<IPermissionService>().HasPermissionAsync(staff900.Id,"CUSTOMER_VIEW",default),"Synthetic staff permission is cached before source status change");
    await Send(SystemEvent("plasma-bv","quanly.plasma.vn","user",900,38,User(900,"Synthetic staff",false),"update"),HttpStatusCode.OK);
    await using(var scope=app.Services.CreateAsyncScope())
        Check(!await scope.ServiceProvider.GetRequiredService<IPermissionService>().HasPermissionAsync(staff900.Id,"CUSTOMER_VIEW",default),"Committed source deactivation invalidates cached employee permissions immediately");
    Check(await Db(c=>c.EmployeeUsers.AnyAsync(x=>x.Id==cachedAccountId && x.Status==UserStatus.Active && x.PasswordHash=="PRESERVE-CACHE-CREDENTIAL")),"Profile status synchronization preserves login account status and credentials");
    await Send(SystemEvent("plasma-bv","quanly.plasma.vn","user",900,39,User(900),"update"),HttpStatusCode.OK);
    await using(var scope=app.Services.CreateAsyncScope())
        Check(await scope.ServiceProvider.GetRequiredService<IPermissionService>().HasPermissionAsync(staff900.Id,"CUSTOMER_VIEW",default),"Verified source reactivation refreshes effective native profile permissions");
    var repairTarget=Guid.NewGuid();
    await Db(async c=>{c.Tanks.Add(new Entity.Tank {Id=repairTarget,TankCode="REPAIR7777",SerialNumber="REPAIR7777",Status=TankStatus.InStock});await c.SaveChangesAsync();return true;});
    await Send(SystemEvent("newapp","newapp.plasmamed.vn","tank",7777,52,JsonSerializer.SerializeToElement(new{id=7777}),"delete"),HttpStatusCode.OK);
    var repairEvent=SystemEvent("newapp","newapp.plasmamed.vn","tank",7777,53,JsonSerializer.SerializeToElement(new {id=7777,serial="REPAIR07777",old_id="",status="ready",customer_id=(long?)null,_capacity=new{id=1,name="5L",value=5},_warehouse=(object?)null,_factory=(object?)null}));
    await Send(repairEvent,HttpStatusCode.Conflict);
    using(var request=new HttpRequestMessage(HttpMethod.Post,"api/legacy-sync/mappings")
    {Content=new StringContent(JsonSerializer.Serialize(new LegacyMappingBindDto {Source="newapp",SourceSystem="newapp.plasmamed.vn",Entity="tank",EntityId=7777,CoreId=repairTarget}),Encoding.UTF8,"application/json")})
    {
        request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
        using var response=await http.SendAsync(request);
        Check(response.StatusCode==HttpStatusCode.OK,"Authorized explicit mapping can resolve a previously null-target source tombstone");
    }
    Check(await Db(c=>c.LegacySyncMappings.AnyAsync(x=>x.Source=="newapp" && x.Entity=="tank" && x.EntityId==7777 && x.CoreId==repairTarget && x.LastSequence==52 && x.IsDeleted)),
        "Administrative null-target resolution preserves prior source revision and tombstone state until source retry");
    await Send(repairEvent,HttpStatusCode.OK);
    Check(await Db(c=>c.Tanks.AnyAsync(x=>x.Id==repairTarget && x.NewAppId==7777 && x.Capacity==5)),"A retained source event succeeds after reviewed null-target mapping without allocating another native row");
    await using(var scope=app.Services.CreateAsyncScope())
    {
        try
        {
            await scope.ServiceProvider.GetRequiredService<ILegacyEventIngestService>().BindMappingAsync(new LegacyMappingBindDto {Source="newapp",SourceSystem="newapp.plasmamed.vn",Entity="tank",EntityId=7777,CoreId=unmatched},default);
            Check(false,"Existing non-null source target must remain immutable");
        }
        catch(CorePlasma.Common.Exceptions.ConflictException){Check(true,"Existing non-null source target cannot be reassigned by administrative bind");}
    }
    // Source create retries can carry a new event UUID. The original system identity, not the
    // delivery event or a coincidentally equal numeric ID from another system, owns the native row.
    string[] upsertEntities = ["customer", "tank", "engine", "user"];
    var upsertSystems = new[] { (Source: "plasma-bv", System: "quanly.plasma.vn"), (Source: "newapp", System: "newapp.plasmamed.vn") };
    Task<int> NativeCount(string entity) => Db(c => entity switch
    {
        "customer" => c.Customers.IgnoreQueryFilters().CountAsync(),
        "tank" => c.Tanks.IgnoreQueryFilters().CountAsync(),
        "engine" => c.Engines.IgnoreQueryFilters().CountAsync(),
        "user" => c.Employees.IgnoreQueryFilters().CountAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(entity))
    });
    JsonElement UpsertSnapshot(string entity, int sourceIndex, long id, bool changed) => entity switch
    {
        "customer" => Customer(id, changed ? "Updated original customer" : "Original customer"),
        "tank" => JsonSerializer.SerializeToElement(new { id, serial = $"UPSERT-T{sourceIndex}-9100", old_id = "", status = "ready", customer_id = (long?)null,
            _capacity = new { id = 1, name = "Synthetic litres", value = changed ? 8 : 5 }, _warehouse = (object?)null, _factory = (object?)null }),
        "engine" => JsonSerializer.SerializeToElement(new { id, name = changed ? "Updated original engine" : "Original engine", serial = $"UPSERT-E{sourceIndex}-9100", old_id = "", status = "active" }),
        "user" => User(id, changed ? "Updated original employee" : "Original employee"),
        _ => throw new ArgumentOutOfRangeException(nameof(entity))
    };
    var upsertCoreIds = new Dictionary<(int Source, string Entity), Guid>();
    for (var sourceIndex = 0; sourceIndex < upsertSystems.Length; sourceIndex++)
    {
        var system = upsertSystems[sourceIndex];
        foreach (var entity in upsertEntities)
        {
            var sequence = 90 + Array.IndexOf(upsertEntities, entity) * 10;
            var nativeBefore = await NativeCount(entity);
            var create = SystemEvent(system.Source, system.System, entity, 9100, sequence, UpsertSnapshot(entity, sourceIndex, 9100, false));
            await Send(create, HttpStatusCode.OK);
            var originalCoreId = (await Db(c => c.LegacySyncMappings.SingleAsync(x => x.Source == system.Source && x.Entity == entity && x.EntityId == 9100))).CoreId!.Value;
            upsertCoreIds.Add((sourceIndex, entity), originalCoreId);
            var repeatedCreate = SystemEvent(system.Source, system.System, entity, 9100, sequence + 1, UpsertSnapshot(entity, sourceIndex, 9100, true));
            await Send(repeatedCreate, HttpStatusCode.OK);
            var mappingAfter = await Db(c => c.LegacySyncMappings.SingleAsync(x => x.Source == system.Source && x.Entity == entity && x.EntityId == 9100));
            Check(create.EventId != repeatedCreate.EventId && mappingAfter.CoreId == originalCoreId && mappingAfter.LastSequence == sequence + 1 &&
                await NativeCount(entity) == nativeBefore + 1,
                $"{system.System} {entity}: a new delivery UUID for the same original ID updates one mapped native row");
            Check((await Send(create, HttpStatusCode.OK))!.Status == "duplicate",
                $"{system.System} {entity}: replaying the first create after a newer create remains a harmless delivery retry");
            var receiptsBefore = await Db(c => c.LegacySyncReceipts.CountAsync());
            await Send(SystemEvent(system.Source, system.System, entity, 9100, sequence + 2, UpsertSnapshot(entity, sourceIndex, 9101, false), "update"), HttpStatusCode.BadRequest);
            var wrongNative = SystemEvent(system.Source, system.System, entity, 9100, sequence + 2, UpsertSnapshot(entity, sourceIndex, 9100, false), "update");
            wrongNative.CoreId = Guid.NewGuid();
            await Send(wrongNative, HttpStatusCode.Conflict);
            var nativeUnchanged = await Db(c => entity switch
            {
                "customer" => c.Customers.AnyAsync(x => x.Id == originalCoreId && x.CustomerName == "Updated original customer" &&
                    (sourceIndex == 0 ? x.QuanLyId == 9100 && x.NewAppId == null : x.NewAppId == 9100 && x.QuanLyId == null)),
                "tank" => c.Tanks.AnyAsync(x => x.Id == originalCoreId && x.Capacity == 8 &&
                    (sourceIndex == 0 ? x.QuanLyId == 9100 && x.NewAppId == null : x.NewAppId == 9100 && x.QuanLyId == null)),
                "engine" => c.Engines.AnyAsync(x => x.Id == originalCoreId && x.EngineName == "Updated original engine" &&
                    (sourceIndex == 0 ? x.QuanLyId == 9100 && x.NewAppId == null : x.NewAppId == 9100 && x.QuanLyId == null)),
                "user" => c.Employees.AnyAsync(x => x.Id == originalCoreId && x.FullName == "Updated original employee" &&
                    (sourceIndex == 0 ? x.QuanLyId == 9100 && x.NewAppId == null : x.NewAppId == 9100 && x.QuanLyId == null)),
                _ => throw new ArgumentOutOfRangeException(nameof(entity))
            });
            Check(nativeUnchanged && await NativeCount(entity) == nativeBefore + 1 && await Db(c => c.LegacySyncReceipts.CountAsync()) == receiptsBefore &&
                await Db(c => c.LegacySyncMappings.AnyAsync(x => x.Source == system.Source && x.Entity == entity && x.EntityId == 9100 && x.LastSequence == sequence + 1)),
                $"{system.System} {entity}: snapshot ID and native GUID mismatches leave native data, source IDs, revision and receipts unchanged");
        }
    }
    foreach (var entity in upsertEntities)
        Check(upsertCoreIds[(0, entity)] != upsertCoreIds[(1, entity)],
            $"{entity}: repeated creates with the same numeric ID in different source systems retain distinct native GUIDs");
    // Exact pair selection intersects old source/entity allowlists without broadening either.
    LegacySyncSettings BoundPolicy(Dictionary<string, string?> values)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddApplicationServices(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<LegacySyncSettings>>().CurrentValue;
    }
    Check(!BoundPolicy(new() { ["LegacySync:AllowedStreams"] = null }).Allows("newapp", "engine"),
        "Actual configuration binding preserves an explicitly empty deny-all matrix");
    var boundMatrix = BoundPolicy(new() { ["LegacySync:AllowedStreams:0:Source"] = "newapp", ["LegacySync:AllowedStreams:0:Entity"] = "engine" });
    Check(boundMatrix.Allows("newapp", "engine") && !boundMatrix.Allows("newapp", "customer") && !boundMatrix.Allows("plasma-bv", "engine"),
        "Actual options binding allows only configured exact stream pairs");
    try { BoundPolicy(new() { ["LegacySync:ProjectionOwners:customer"] = "newapp" }); Check(false, "Invalid projection owner must fail validation"); }
    catch (OptionsValidationException) { Check(true, "Actual options validation rejects unsupported projection ownership"); }
    Check(new LegacySyncSettings().Allows("newapp", "engine"), "Absent matrix preserves the existing unrestricted supported stream selection");
    Check(!new LegacySyncSettings { AllowedStreams = [] }.Allows("newapp", "engine"), "Explicit empty receive matrix denies every stream");
    Check(!new LegacySyncSettings { AllowedStreams = [new() { Source = "newapp", Entity = "bad" }] }.IsValid() &&
        !new LegacySyncSettings { ProjectionOwners = new() { ["customer"] = "newapp" } }.IsValid(),
        "Unsupported pair and customer projection owner are rejected as configuration errors");
    policy.Value.AllowedEntities = ["engine", "customer"];
    policy.Value.AllowedSources = ["newapp", "plasma-bv"];
    policy.Value.AllowedStreams = [new() { Source = "newapp", Entity = "engine" }, new() { Source = "plasma-bv", Entity = "customer" }];
    var matrixRejected = SystemEvent("newapp", "newapp.plasmamed.vn", "customer", 9200, 100000, Customer(9200));
    var matrixCount = await Db(c => c.LegacySyncReceipts.CountAsync());
    await Send(matrixRejected, HttpStatusCode.Forbidden);
    await Send(SystemEvent("plasma-bv", "quanly.plasma.vn", "engine", 9200, 100000, Engine(9200, "MATRIX-EXCLUDED")), HttpStatusCode.Forbidden);
    Check(await Db(c => c.LegacySyncReceipts.CountAsync()) == matrixCount, "Independent allowlists cannot accidentally enable the source/entity Cartesian product");
    await Send(SystemEvent("plasma-bv", "quanly.plasma.vn", "customer", 9200, 100000, Customer(9200)), HttpStatusCode.OK);
    await Send(SystemEvent("newapp", "newapp.plasmamed.vn", "engine", 9200, 100000, Engine(9200, "MATRIX-ENGINE")), HttpStatusCode.OK);
    policy.Value.AllowedSources = ["plasma-bv"];
    await Send(SystemEvent("newapp", "newapp.plasmamed.vn", "engine", 9201, 100001, Engine(9201, "MATRIX-ENGINE-BLOCKED")), HttpStatusCode.Forbidden);
    policy.Value.AllowedEntities = null; policy.Value.AllowedSources = null; policy.Value.AllowedStreams = null;

    foreach (var entity in new[] { "tank", "engine", "user" })
    {
        var originalId = upsertCoreIds[(0, entity)];
        var secondId = 9200 + Array.IndexOf(new[] { "tank", "engine", "user" }, entity) + 10;
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ILegacyEventIngestService>().BindMappingAsync(new LegacyMappingBindDto
                { Source = "newapp", SourceSystem = "newapp.plasmamed.vn", Entity = entity, EntityId = secondId, CoreId = originalId }, default);
        policy.Value.ProjectionOwners.Remove(entity);
        var next = SystemEvent("newapp", "newapp.plasmamed.vn", entity, secondId, 100100 + secondId,
            UpsertSnapshot(entity, 0, secondId, false), "update");
        var receiptsBefore = await Db(c => c.LegacySyncReceipts.CountAsync());
        await Send(next, HttpStatusCode.Conflict);
        Check(await Db(c => c.LegacySyncReceipts.CountAsync()) == receiptsBefore,
            $"{entity}: shared records require an explicit owner rather than applying last-writer projection");
        policy.Value.ProjectionOwners[entity] = "plasma-bv";
        await Send(next, HttpStatusCode.OK);
        var alternate = await Db(c => c.LegacySyncMappings.SingleAsync(x => x.Source == "newapp" && x.Entity == entity && x.EntityId == secondId));
        Check(alternate.CoreId == originalId && alternate.LastSequence == next.Sequence && System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(alternate.SnapshotJson), System.Text.Json.Nodes.JsonNode.Parse(next.Data.GetRawText())) &&
            await Db(c => c.LegacySyncReceipts.AnyAsync(x => x.EventId == next.EventId && x.Status == "applied")),
            $"{entity}: alternate source snapshot and receipt advance without reallocating identity");
        Check(await Db(c => entity switch
        {
            "tank" => c.Tanks.AnyAsync(x => x.Id == originalId && x.Capacity == 8),
            "engine" => c.Engines.AnyAsync(x => x.Id == originalId && x.EngineName == "Updated original engine"),
            "user" => c.Employees.AnyAsync(x => x.Id == originalId && x.FullName == "Updated original employee"),
            _ => throw new InvalidOperationException()
        }), $"{entity}: alternate source cannot overwrite primary business fields");
        var primaryUpdate = SystemEvent("plasma-bv", "quanly.plasma.vn", entity, 9100, next.Sequence + 1,
            UpsertSnapshot(entity, 0, 9100, false), "update");
        await Send(primaryUpdate, HttpStatusCode.OK);
        Check(await Db(c => entity switch
        {
            "tank" => c.Tanks.AnyAsync(x => x.Id == originalId && x.Capacity == 5),
            "engine" => c.Engines.AnyAsync(x => x.Id == originalId && x.EngineName == "Original engine"),
            "user" => c.Employees.AnyAsync(x => x.Id == originalId && x.FullName == "Original employee"),
            _ => throw new InvalidOperationException()
        }), $"{entity}: configured owner still projects new native state");
    }
    using var bind = new HttpRequestMessage(HttpMethod.Post, "api/legacy-sync/mappings") { Content = new StringContent(JsonSerializer.Serialize(new LegacyMappingBindDto { Source = "plasma-bv", Entity = "customer", EntityId = 300, CoreId = canonicalId }), Encoding.UTF8, "application/json") };
    bind.Headers.TryAddWithoutValidation("X-API-Key", key);
    using var bindResponse = await http.SendAsync(bind);
    Check(bindResponse.StatusCode == HttpStatusCode.Unauthorized, "Shared integration API key cannot bind arbitrary native identities through the administrative endpoint");
    Console.WriteLine($"PASS: {checks} synthetic legacy synchronization checks.");
}
finally
{
    http?.Dispose(); await app.StopAsync();
    NpgsqlConnection.ClearAllPools();
    await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
    await using var cleanup = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin);
    await cleanup.ExecuteNonQueryAsync();
    Console.WriteLine("PASS: owned disposable test database removed.");
}

sealed class TestLegacyPolicy : IOptionsMonitor<LegacySyncSettings>
{
    public LegacySyncSettings Value { get; } = new();
    public LegacySyncSettings CurrentValue => Value;
    public LegacySyncSettings Get(string? name) => Value;
    public IDisposable? OnChange(Action<LegacySyncSettings, string?> listener) => null;
}
