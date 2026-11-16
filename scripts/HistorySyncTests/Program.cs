using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CorePlasma.API.Authentication;
using CorePlasma.API.Controllers;
using CorePlasma.API.Extensions;
using CorePlasma.BAL.DTOs.Integration;
using CorePlasma.BAL.Extensions;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Enums;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using CoreEntity = CorePlasma.DAL.Entities;

// Npgsql caches this switch on first use, including the administrative CREATE DATABASE connection.
// Match Core DAL's UTC wall-clock / timestamp-without-time-zone contract before touching Npgsql.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var suite = new TestSuite();
try
{
    await using var database = await DisposablePostgres.CreateAsync();
    var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    var jwtSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    await using var host = await CoreHistoryHost.StartAsync(database.ConnectionString, key, jwtSecret);
    await host.DbAsync(async context => { await context.Database.MigrateAsync(); return true; });
    suite.Check(true, "Core migrations applied only to a newly created disposable test database");
    await ReceiverCases.RunAsync(host, suite, key, jwtSecret);
    await SourceCases.RunAsync(host, suite, database.ConnectionString, key);
    Console.WriteLine($"PASS: {suite.Count} history synchronization checks using synthetic records.");
    return 0;
}
catch (Exception exception)
{
    // Never dump connection strings, payloads, API credentials or exception stack traces.
    Console.Error.WriteLine($"FAIL: {exception.GetType().Name}: {(exception is TestFailure ? exception.Message : "History synchronization test infrastructure failed.")}");
    for (Exception? nested = exception; nested is not null; nested = nested.InnerException)
    {
        if (nested is not PostgresException postgres) continue;
        Console.Error.WriteLine($"POSTGRES: SqlState={postgres.SqlState}; Constraint={postgres.ConstraintName}; Table={postgres.TableName}; Column={postgres.ColumnName}; Message={postgres.MessageText}");
    }
    var innermost = exception;
    while (innermost.InnerException is { } inner) innermost = inner;
    Console.Error.WriteLine("INNER TYPE: " + innermost.GetType().FullName);
    return 1;
}

static class ReceiverCases
{
    private static long revision = 100_000; // Separate from source outbox sequence IDs in the same test database.
    internal static readonly Guid EngineA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    internal static readonly Guid TankA = Guid.Parse("22222222-2222-4222-8222-222222222222");

    public static HistorySessionIngestDto Snapshot(long sourceId, long childId, string serial = "SYNTHETIC-ENGINE-A") => new()
    {
        Source = "NEWAPP", EventId = Guid.NewGuid(), SourceRevision = Interlocked.Increment(ref revision), SourceSessionId = sourceId,
        EngineSerial = serial, StartTime = new DateTimeOffset(2026, 10, 12, 3, 4, 5, TimeSpan.Zero),
        EndTime = new DateTimeOffset(2026, 10, 12, 3, 14, 5, TimeSpan.Zero), Mode = "synthetic", UserCode = "synthetic-user-code",
        AverageSpeed = "3.25", EnvironmentTemperatureBefore = "12", EnvironmentTemperatureAfter = "13",
        TankSessions = [new() { SourceTankSessionId = childId, TankRfid = "SYNTHETIC-TANK-A", TankSerial = "SYNTHETIC-TANK-A",
            TankLegacyId = "synthetic-legacy-tank-a", Order = 1, HighPressureAfter = "12500", HighPressureBefore = "11250",
            LoadCellAfter = "40.50", LoadCellBefore = "39.25" }]
    };

    private static HistorySessionIngestDto Copy(HistorySessionIngestDto dto) =>
        JsonSerializer.Deserialize<HistorySessionIngestDto>(JsonSerializer.Serialize(dto))!;

    public static async Task RunAsync(CoreHistoryHost host, TestSuite suite, string key, string jwtSecret)
    {
        await host.DbAsync(async context =>
        {
            context.Engines.AddRange(
                new CoreEntity.Engine { Id = EngineA, EngineCode = "SYNTHETIC-ENGINE-A-CODE", SerialNumber = "SYNTHETIC-ENGINE-A", EngineName = "Synthetic A", NewAppId = 7001 },
                new CoreEntity.Engine { EngineCode = "SYNTHETIC-ENGINE-B-CODE", SerialNumber = "SYNTHETIC-ENGINE-B", EngineName = "Synthetic B" },
                new CoreEntity.Engine { EngineCode = "SYNTHETIC-AMBIGUOUS-A", SerialNumber = "SYNTHETIC-AMBIGUOUS", EngineName = "Synthetic ambiguous A" },
                new CoreEntity.Engine { EngineCode = "SYNTHETIC-AMBIGUOUS-B", SerialNumber = "SYNTHETIC-AMBIGUOUS", EngineName = "Synthetic ambiguous B" });
            context.Tanks.AddRange(
                new CoreEntity.Tank { Id = TankA, TankCode = "SYNTHETIC-TANK-A", SerialNumber = "SYNTHETIC-TANK-A", LegacyId = "synthetic-legacy-tank-a", Status = TankStatus.AtClient },
                new CoreEntity.Tank { TankCode = "SYNTHETIC-TANK-B", SerialNumber = "SYNTHETIC-TANK-B", LegacyId = "synthetic-legacy-tank-b" },
                new CoreEntity.Tank { TankCode = "SYNTHETIC-AMBIGUOUS-TANK-A", SerialNumber = "SYNTHETIC-AMBIGUOUS-RFID" },
                new CoreEntity.Tank { TankCode = "SYNTHETIC-AMBIGUOUS-TANK-B", SerialNumber = "SYNTHETIC-AMBIGUOUS-RFID" });
            await context.SaveChangesAsync();
            return true;
        });

        var dto = Snapshot(10001, 100011);
        var baseline = await host.FingerprintAsync();
        foreach (var credentials in new string[]?[] { null, ["wrong-test-key"], [key, key] })
            await host.ExpectAsync(dto, HttpStatusCode.Unauthorized, suite, "Receiver rejects missing, wrong or duplicate API-key credentials", credentials);
        await host.ExpectAsync(dto, HttpStatusCode.Unauthorized, suite, "Employee JWT alone cannot write the integration history endpoint", bearer: host.Jwt(jwtSecret));
        suite.Check(await host.FingerprintAsync() == baseline, "Rejected credentials do not create source-history or acknowledgement rows");

        var applied = await host.ExpectAsync(dto, HttpStatusCode.OK, suite, "Committed source snapshot is accepted", [key]);
        suite.Check(applied!.Disposition == "applied" && applied.TankSessionsUpserted == 1 && applied.SourceSessionId == dto.SourceSessionId,
            "Receiver acknowledgement identifies the applied event and its child count");
        var stored = await host.DbAsync(context => context.Sessions.AsNoTracking().Include(row => row.TankSessions).SingleAsync(row => row.LegacyId == dto.SourceSessionId));
        var child = stored.TankSessions.Single();
        suite.Check(stored.StartTime == dto.StartTime.UtcDateTime && stored.EndTime == dto.EndTime!.Value.UtcDateTime,
            "Source UTC timestamps retain their exact wall-clock values without another seven-hour conversion");
        suite.Check(stored.EngineId == EngineA && child.TankId == TankA && child.LegacyId == dto.TankSessions[0].SourceTankSessionId,
            "Source IDs map to the intended Core graph and unique tank hints");
        suite.Check(child.PressureBefore == 12500m && child.PressureAfter == 11250m && child.WeightBefore == 40.50m && child.WeightAfter == 39.25m,
            "NEWAPP pressure and load-cell Before/After labels are swapped exactly once");
        var beforeReplay = await host.FingerprintAsync();
        var duplicate = await host.ExpectAsync(dto, HttpStatusCode.OK, suite, "Exact delivery replay receives a durable acknowledgement", [key]);
        suite.Check(duplicate!.Disposition == "duplicate" && duplicate.TankSessionsUpserted == 0 && duplicate.SessionId == applied.SessionId,
            "Replay returns the same Core session without another child write");
        suite.Check(await host.FingerprintAsync() == beforeReplay, "Replay leaves graph, receipt and audit rows unchanged");

        var stale = Copy(dto); stale.EventId = Guid.NewGuid(); stale.SourceRevision = dto.SourceRevision - 1; stale.Mode = "stale-attempt";
        var staleAck = await host.ExpectAsync(stale, HttpStatusCode.OK, suite, "Delayed older revision is acknowledged as stale", [key]);
        suite.Check(staleAck!.Disposition == "stale" && staleAck.TankSessionsUpserted == 0,
            "Stale delivery is durable and cannot overwrite newer source state");
        suite.Check(await host.DbAsync(context => context.Sessions.AsNoTracking().Where(row => row.LegacyId == dto.SourceSessionId).Select(row => row.Mode).SingleAsync()) == dto.Mode,
            "Newer session values survive stale delivery");

        var changedEvent = Copy(dto); changedEvent.Mode = "conflicting-replay";
        await RejectWithoutChanges(host, changedEvent, HttpStatusCode.Conflict, suite, key, "Event ID cannot be reused for another payload");
        var reusedRevision = Copy(dto); reusedRevision.EventId = Guid.NewGuid(); reusedRevision.SourceSessionId = 77777;
        await RejectWithoutChanges(host, reusedRevision, HttpStatusCode.Conflict, suite, key, "Global outbox revision cannot be reused for another source session");

        var update = Copy(dto); update.EventId = Guid.NewGuid(); update.SourceRevision = Interlocked.Increment(ref revision);
        update.Mode = "updated-snapshot"; update.UserCode = null; update.TankSessions[0].HighPressureBefore = "10000";
        await host.ExpectAsync(update, HttpStatusCode.OK, suite, "New source revision updates the existing session graph", [key]);
        var updated = await host.DbAsync(context => context.Sessions.AsNoTracking().Include(row => row.TankSessions).SingleAsync(row => row.LegacyId == dto.SourceSessionId));
        suite.Check(updated.Id == applied.SessionId && updated.UserCode == dto.UserCode && updated.TankSessions.Single().Id == child.Id && updated.TankSessions.Single().PressureAfter == 10000m,
            "Update preserves IDs and an existing user code when the legacy source omits it");

        foreach (var kind in new[] { "source", "missing-engine", "ambiguous-engine", "owner", "number", "precision", "offset", "duplicate-child" })
        {
            var bad = Snapshot(20000 + revision, 30000 + revision);
            var status = HttpStatusCode.BadRequest;
            switch (kind)
            {
                case "source": bad.Source = "PLT"; break;
                case "missing-engine": bad.EngineSerial = "SYNTHETIC-MISSING"; status = HttpStatusCode.Conflict; break;
                case "ambiguous-engine": bad.EngineSerial = "SYNTHETIC-AMBIGUOUS"; status = HttpStatusCode.Conflict; break;
                case "owner": bad.TankSessions[0].SourceTankSessionId = child.LegacyId!.Value; status = HttpStatusCode.Conflict; break;
                case "number": bad.TankSessions[0].HighPressureBefore = "1,25"; break;
                case "precision": bad.TankSessions[0].HighPressureBefore = "1.23456789012345678901234567890123456789"; break;
                case "offset": bad.StartTime = bad.StartTime.ToOffset(TimeSpan.FromHours(7)); break;
                case "duplicate-child": bad.TankSessions.Add(bad.TankSessions[0]); break;
            }
            await RejectWithoutChanges(host, bad, status, suite, key, $"Receiver rejects invalid {kind} without a partial graph or receipt");
        }

        var wrongEngineOwner = Copy(update); wrongEngineOwner.EventId = Guid.NewGuid(); wrongEngineOwner.SourceRevision = Interlocked.Increment(ref revision); wrongEngineOwner.EngineSerial = "SYNTHETIC-ENGINE-B";
        await RejectWithoutChanges(host, wrongEngineOwner, HttpStatusCode.Conflict, suite, key, "Existing source session cannot be reassigned to another Core engine");

        var rollback = Copy(update); rollback.EventId = Guid.NewGuid(); rollback.SourceRevision = Interlocked.Increment(ref revision); rollback.Mode = "must-roll-back";
        rollback.TankSessions[0].HighPressureBefore = "123";
        rollback.TankSessions.Add(new() { SourceTankSessionId = 400_000, TankSerial = "SYNTHETIC-TANK-B", TankLegacyId = "synthetic-legacy-tank-a", HighPressureAfter = "1", HighPressureBefore = "0" });
        await RejectWithoutChanges(host, rollback, HttpStatusCode.Conflict, suite, key, "Conflicting second child rolls back earlier parent and child changes atomically");

        var ambiguousRfid = Snapshot(50001, 500011); ambiguousRfid.TankSessions[0].TankSerial = null; ambiguousRfid.TankSessions[0].TankLegacyId = null; ambiguousRfid.TankSessions[0].TankRfid = "SYNTHETIC-AMBIGUOUS-RFID";
        await host.ExpectAsync(ambiguousRfid, HttpStatusCode.OK, suite, "Ambiguous RFID keeps source history without inventing a tank mapping", [key]);
        suite.Check(await host.DbAsync(context => context.TankSessions.AsNoTracking().Where(row => row.LegacyId == 500011).Select(row => row.TankId).SingleAsync()) is null,
            "Ambiguous RFID persists a null Core tank link");

        var concurrent = Snapshot(60001, 600011);
        var concurrentAcks = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => host.ExpectAsync(concurrent, HttpStatusCode.OK, suite, "Concurrent identical delivery succeeds safely", [key])));
        suite.Check(concurrentAcks.Count(ack => ack!.Disposition == "applied") == 1 && concurrentAcks.Count(ack => ack!.Disposition == "duplicate") == 3,
            "Concurrent delivery creates one committed receipt and one source graph");
        suite.Check(await host.DbAsync(context => context.Sessions.CountAsync(row => row.LegacyId == 60001)) == 1 &&
                    await host.DbAsync(context => context.TankSessions.CountAsync(row => row.LegacyId == 600011)) == 1,
            "Concurrent delivery cannot duplicate parent or child records");

        var older = Snapshot(70001, 700011); older.Mode = "older-concurrent";
        var newer = Copy(older); newer.EventId = Guid.NewGuid(); newer.SourceRevision = Interlocked.Increment(ref revision); newer.Mode = "newer-concurrent";
        await Task.WhenAll(host.ExpectAsync(older, HttpStatusCode.OK, suite, "Concurrent older revision is acknowledged", [key]),
            host.ExpectAsync(newer, HttpStatusCode.OK, suite, "Concurrent newer revision is acknowledged", [key]));
        suite.Check(await host.DbAsync(context => context.Sessions.AsNoTracking().Where(row => row.LegacyId == 70001).Select(row => row.Mode).SingleAsync()) == "newer-concurrent",
            "Out-of-order concurrent revisions converge to the latest source revision");

        var raw = JsonSerializer.SerializeToNode(Snapshot(80001, 800011), CoreHistoryHost.Json)!;
        raw["startTime"] = "2026-10-12T03:04:05";
        var beforeBadTime = await host.FingerprintAsync();
        using (var response = await host.PostRawAsync(raw.ToJsonString(), [key]))
            suite.Check(response.StatusCode == HttpStatusCode.BadRequest, "Wire timestamp without an explicit UTC offset is rejected");
        suite.Check(await host.FingerprintAsync() == beforeBadTime, "Invalid wire timestamp leaves no graph or receipt");

        foreach (var (method, path) in new[] { (HttpMethod.Post, "api/engines"), (HttpMethod.Put, $"api/tanks/{TankA}"), (HttpMethod.Delete, "api/customers/33333333-3333-4333-8333-333333333333") })
        {
            using var request = new HttpRequestMessage(method, path); request.Headers.Add(ApiKeyOptions.HeaderName, key); request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await host.Http.SendAsync(request);
            suite.Check(response.StatusCode == HttpStatusCode.Unauthorized, "Integration key cannot write JWT business routes");
        }

        // The later NEWAPP ID is authoritative even when an old packet arrives later with a faulty device clock.
        var latestUsage = Snapshot(90002, 900021); latestUsage.TankSessions[0].HighPressureBefore = "10000";
        await host.ExpectAsync(latestUsage, HttpStatusCode.OK, suite, "Newest source tank usage is received", [key]);
        var delayedUsage = Snapshot(90001, 900011); delayedUsage.StartTime = new DateTimeOffset(2032, 1, 1, 0, 0, 0, TimeSpan.Zero); delayedUsage.EndTime = delayedUsage.StartTime.AddMinutes(5); delayedUsage.TankSessions[0].HighPressureBefore = "1250";
        await host.ExpectAsync(delayedUsage, HttpStatusCode.OK, suite, "Older source usage with a faulty future clock is retained", [key]);
        using var tankRequest = new HttpRequestMessage(HttpMethod.Get, $"api/tanks/{TankA}"); tankRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", host.Jwt(jwtSecret));
        using var tankResponse = await host.Http.SendAsync(tankRequest);
        suite.Check(tankResponse.StatusCode == HttpStatusCode.OK, "Authorized employee can read synchronized tank data");
        using var tankJson = JsonDocument.Parse(await tankResponse.Content.ReadAsStringAsync());
        suite.Check(tankJson.RootElement.GetProperty("data").GetProperty("gasRemainingPercent").GetDecimal() == 80m,
            "Latest tank reading follows source history IDs rather than packet arrival or a faulty device clock");

        await LegacyReceiptCompatibilityAsync(host, suite, key);
        await StableEngineIdentityAsync(host, suite, key);
        await StableTankIdentityAsync(host, suite, key);
    }

    private static async Task LegacyReceiptCompatibilityAsync(CoreHistoryHost host, TestSuite suite, string key)
    {
        // Pinned before SourceEngineId was introduced, using the old production DTO serializer.
        const string oldHash = "3D8E5101BC8AA5901C97D6D0D57D38F8A550B7A3F065A1D78C0665DDB085EF15";
        var legacy = new HistorySessionIngestDto
        {
            Source = "NEWAPP", EventId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), SourceRevision = 12345,
            SourceSessionId = 54321, EngineSerial = "LEGACY-HASH-ENGINE",
            StartTime = new DateTimeOffset(2026, 10, 12, 3, 4, 5, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 10, 12, 3, 14, 5, TimeSpan.Zero),
            Mode = "legacy", UserCode = "synthetic", TankSessions = []
        };
        suite.Check(Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(legacy))) == oldHash,
            "Adding an omitted nullable SourceEngineId preserves the exact pre-change normalized payload hash");
        using (var normalized = JsonDocument.Parse(JsonSerializer.Serialize(legacy)))
            suite.Check(!normalized.RootElement.TryGetProperty("SourceEngineId", out _),
                "Absent legacy SourceEngineId is omitted rather than serialized as a new null hash field");
        var target = await host.DbAsync(async context =>
        {
            var row = new CoreEntity.Session { EngineId = EngineA, LegacyId = legacy.SourceSessionId,
                SourceRevision = legacy.SourceRevision, StartTime = legacy.StartTime.UtcDateTime, EndTime = legacy.EndTime!.Value.UtcDateTime };
            context.Sessions.Add(row);
            context.HistoryIngestReceipts.Add(new CoreEntity.HistoryIngestReceipt
            {
                EventId = legacy.EventId, Source = "NEWAPP", SourceSessionId = legacy.SourceSessionId,
                SourceRevision = legacy.SourceRevision, SessionId = row.Id, PayloadHash = oldHash,
                Disposition = "applied", ReceivedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            return row.Id;
        });
        var ack = await host.ExpectAsync(legacy, HttpStatusCode.OK, suite,
            "A receipt written by the previous DTO release still acknowledges an unchanged legacy replay", [key]);
        suite.Check(ack!.Disposition == "duplicate" && ack.SessionId == target,
            "Pre-upgrade receipt retains its original graph identity without resolving a now-absent serial");
        var changed = Copy(legacy); changed.SourceEngineId = 7001;
        await RejectWithoutChanges(host, changed, HttpStatusCode.Conflict, suite, key,
            "Adding an engine identity to an already-received EventId remains a conflicting payload change");
    }

    private static async Task StableEngineIdentityAsync(CoreHistoryHost host, TestSuite suite, string key)
    {
        var engineId = Guid.NewGuid(); var mappingOnlyId = Guid.NewGuid();
        var mismatchedColumnId = Guid.NewGuid(); var mismatchedMappingId = Guid.NewGuid();
        var deletedAliasId = Guid.NewGuid(); var deletedEngineId = Guid.NewGuid(); var otherSourceId = Guid.NewGuid();
        await host.DbAsync(async context =>
        {
            context.Engines.AddRange(
                new CoreEntity.Engine { Id = engineId, NewAppId = 9001, EngineCode = "IDENTITY-CODE", SerialNumber = "IDENTITY-OLD-SERIAL", EngineName = "Stable identity" },
                new CoreEntity.Engine { Id = mappingOnlyId, EngineCode = "MAPPING-ONLY-CODE", SerialNumber = "MAPPING-ONLY-SERIAL", EngineName = "Verified mapping only" },
                new CoreEntity.Engine { Id = mismatchedColumnId, NewAppId = 9003, EngineCode = "CONFLICT-COLUMN", EngineName = "Conflicting identity column" },
                new CoreEntity.Engine { Id = mismatchedMappingId, EngineCode = "CONFLICT-MAPPING", EngineName = "Conflicting identity mapping" },
                new CoreEntity.Engine { Id = deletedAliasId, NewAppId = 9004, EngineCode = "DELETED-ALIAS", EngineName = "Deleted source alias" },
                new CoreEntity.Engine { Id = deletedEngineId, NewAppId = 9005, EngineCode = "DELETED-ENGINE", EngineName = "Deleted target", IsDeleted = true },
                new CoreEntity.Engine { Id = otherSourceId, NewAppId = 9007, EngineCode = "OTHER-SOURCE-ID", EngineName = "Bound to another source ID" });
            CoreEntity.LegacySyncMapping Mapping(long id, Guid target, bool deleted = false) => new()
            {
                Source = "newapp", SourceSystem = "newapp.plasmamed.vn", Entity = "engine", EntityId = id,
                CoreId = target, IsDeleted = deleted, SnapshotJson = "{}", UpdatedAt = DateTime.UtcNow
            };
            context.LegacySyncMappings.AddRange(Mapping(9002, mappingOnlyId), Mapping(9003, mismatchedMappingId),
                Mapping(9004, deletedAliasId, true), Mapping(9006, otherSourceId));
            await context.SaveChangesAsync();
            // The production audit interceptor deliberately initializes Added rows as active.
            // Exercise its real soft-delete path rather than pre-setting a discarded Added flag.
            context.Engines.Remove(await context.Engines.SingleAsync(x => x.Id == deletedEngineId));
            await context.SaveChangesAsync();
            return true;
        });
        var original = Snapshot(110001, 1100011, "IDENTITY-OLD-SERIAL"); original.SourceEngineId = 9001;
        var first = await host.ExpectAsync(original, HttpStatusCode.OK, suite,
            "A protected native NewAppId resolves the verified source engine identity", [key]);
        suite.Check(await host.DbAsync(context => context.Sessions.Where(x => x.LegacyId == original.SourceSessionId)
            .Select(x => x.EngineId).SingleAsync()) == engineId, "Stable source engine ID chooses the intended native GUID");
        await host.DbAsync(async context =>
        {
            (await context.Engines.SingleAsync(x => x.Id == engineId)).SerialNumber = "IDENTITY-RENAMED-SERIAL";
            context.Engines.Add(new CoreEntity.Engine { EngineCode = "REUSED-OLD-SERIAL-CODE", SerialNumber = "IDENTITY-OLD-SERIAL", EngineName = "Another physical identity" });
            await context.SaveChangesAsync(); return true;
        });
        var olderSerial = Copy(original); olderSerial.EventId = Guid.NewGuid(); olderSerial.SourceRevision = Interlocked.Increment(ref revision);
        olderSerial.Mode = "after-serial-change";
        var renamed = await host.ExpectAsync(olderSerial, HttpStatusCode.OK, suite,
            "An immutable packet with the old serial stays on its verified engine even after that serial is reused", [key]);
        suite.Check(renamed!.SessionId == first!.SessionId && await host.DbAsync(context =>
            context.Sessions.Where(x => x.LegacyId == original.SourceSessionId).Select(x => x.EngineId).SingleAsync()) == engineId,
            "Machine serial changes never move existing history to another Core GUID");
        var mappingOnly = Snapshot(110002, 1100021, "OLD-MAPPING-ONLY-SERIAL"); mappingOnly.SourceEngineId = 9002;
        await host.ExpectAsync(mappingOnly, HttpStatusCode.OK, suite, "An explicit verified mapping works before its optional native ID column is populated", [key]);
        suite.Check(await host.DbAsync(context => context.Sessions.Where(x => x.LegacyId == mappingOnly.SourceSessionId)
            .Select(x => x.EngineId).SingleAsync()) == mappingOnlyId, "Mapping-only resolution uses its audited target GUID");

        foreach (var (sourceId, expected, label) in new[]
        {
            (0L, HttpStatusCode.BadRequest, "nonpositive source engine identity"),
            (-1L, HttpStatusCode.BadRequest, "negative source engine identity"),
            (999999L, HttpStatusCode.Conflict, "unknown source engine identity despite an otherwise valid serial"),
            (9003L, HttpStatusCode.Conflict, "mapping and source-ID column disagree"),
            (9004L, HttpStatusCode.Conflict, "deleted source engine alias"),
            (9005L, HttpStatusCode.Conflict, "deleted native engine"),
            (9006L, HttpStatusCode.Conflict, "mapped target bound to another source ID")
        })
        {
            var bad = Snapshot(120000 + revision, 130000 + revision); bad.SourceEngineId = sourceId;
            await RejectWithoutChanges(host, bad, expected, suite, key, "Receiver rejects " + label + " without serial fallback or partial history");
        }
    }

    private static async Task StableTankIdentityAsync(CoreHistoryHost host, TestSuite suite, string key)
    {
        var primary = Guid.NewGuid(); var mappingOnly = Guid.NewGuid(); var conflictColumn = Guid.NewGuid();
        var conflictMapping = Guid.NewGuid(); var deletedAlias = Guid.NewGuid(); var deletedNative = Guid.NewGuid();
        var otherId = Guid.NewGuid(); var rfidOnly = Guid.NewGuid(); var ambiguousA = Guid.NewGuid(); var ambiguousB = Guid.NewGuid();
        await host.DbAsync(async context =>
        {
            CoreEntity.Tank Tank(Guid id, string code, long? sourceId = null) => new() { Id = id, TankCode = code, SerialNumber = code, NewAppId = sourceId };
            context.Tanks.AddRange(Tank(primary, "TANK-ID-OLD-SERIAL", 19001), Tank(mappingOnly, "TANK-MAPPING-ONLY"),
                Tank(conflictColumn, "TANK-CONFLICT-COLUMN", 19003), Tank(conflictMapping, "TANK-CONFLICT-MAPPING"),
                Tank(deletedAlias, "TANK-DELETED-ALIAS", 19004), Tank(deletedNative, "TANK-DELETED-NATIVE", 19005),
                Tank(otherId, "TANK-OTHER-ID", 19007), Tank(rfidOnly, "TANK-RFID-ONLY"),
                Tank(ambiguousA, "TANK-RFID-AMBIG-A"), Tank(ambiguousB, "TANK-RFID-AMBIG-B"));
            CoreEntity.LegacySyncMapping Mapping(long id, Guid target, bool deleted = false, string? rfid = null) => new()
            {
                Source = "newapp", SourceSystem = "newapp.plasmamed.vn", Entity = "tank", EntityId = id,
                CoreId = target, IsDeleted = deleted, SnapshotJson = JsonSerializer.Serialize(new { id, rfid }), UpdatedAt = DateTime.UtcNow
            };
            context.LegacySyncMappings.AddRange(Mapping(19002, mappingOnly), Mapping(19003, conflictMapping), Mapping(19004, deletedAlias, true),
                Mapping(19006, otherId), Mapping(19008, rfidOnly, rfid: "RFID-VERIFIED-ONLY"),
                Mapping(19009, ambiguousA, rfid: "RFID-VERIFIED-AMBIG"), Mapping(19010, ambiguousB, rfid: "RFID-VERIFIED-AMBIG"));
            await context.SaveChangesAsync();
            context.Tanks.Remove(await context.Tanks.SingleAsync(x => x.Id == deletedNative));
            await context.SaveChangesAsync(); return true;
        });
        HistorySessionIngestDto Packet(long id, long? sourceId) {
            var dto = Snapshot(id, id * 10 + 1); dto.TankSessions[0].SourceTankId = sourceId; return dto;
        }
        var original = Packet(210001, 19001);
        await host.ExpectAsync(original, HttpStatusCode.OK, suite, "Verified native NewAppId resolves the stable tank identity", [key]);
        suite.Check(await host.DbAsync(c => c.TankSessions.Where(x => x.LegacyId == 2100011).Select(x => x.TankId).SingleAsync()) == primary,
            "Stable tank ID wins over obsolete legacy serial hints without creating a source mapping");
        await host.DbAsync(async c => { (await c.Tanks.SingleAsync(x => x.Id == primary)).SerialNumber = "TANK-ID-RENAMED"; await c.SaveChangesAsync(); return true; });
        var renamed = Copy(original); renamed.EventId = Guid.NewGuid(); renamed.SourceRevision = Interlocked.Increment(ref revision);
        await host.ExpectAsync(renamed, HttpStatusCode.OK, suite, "Old immutable hints remain safe after a tank serial rename", [key]);
        var onlyMapping = Packet(210002, 19002);
        await host.ExpectAsync(onlyMapping, HttpStatusCode.OK, suite, "Verified tank mapping works without a representative native ID column", [key]);
        suite.Check(await host.DbAsync(c => c.TankSessions.Where(x => x.LegacyId == 2100021).Select(x => x.TankId).SingleAsync()) == mappingOnly,
            "Mapping-only tank identity selects its existing GUID");
        foreach (var (id, status) in new[] { (0L, HttpStatusCode.BadRequest), (-1L, HttpStatusCode.BadRequest),
            (19999L, HttpStatusCode.Conflict), (19003L, HttpStatusCode.Conflict), (19004L, HttpStatusCode.Conflict),
            (19005L, HttpStatusCode.Conflict), (19006L, HttpStatusCode.Conflict) })
            await RejectWithoutChanges(host, Packet(220000 + revision, id), status, suite, key,
                "Invalid, unknown, deleted or conflicting stable tank ID never falls back to another serial");
        var reassigned = Copy(original); reassigned.EventId = Guid.NewGuid(); reassigned.SourceRevision = Interlocked.Increment(ref revision);
        reassigned.TankSessions[0].SourceTankId = 19002;
        await RejectWithoutChanges(host, reassigned, HttpStatusCode.Conflict, suite, key,
            "An existing source tank-session cannot be moved to a different physical tank");
        var unresolved = Copy(original); unresolved.EventId = Guid.NewGuid(); unresolved.SourceRevision = Interlocked.Increment(ref revision);
        unresolved.TankSessions[0].SourceTankId = null; unresolved.TankSessions[0].TankSerial = null;
        unresolved.TankSessions[0].TankLegacyId = null; unresolved.TankSessions[0].TankRfid = "UNRESOLVED-OLD-HINT";
        await host.ExpectAsync(unresolved, HttpStatusCode.OK, suite, "Unresolved legacy hints may update measures without erasing an existing tank relationship", [key]);
        suite.Check(await host.DbAsync(c => c.TankSessions.Where(x => x.LegacyId == 2100011).Select(x => x.TankId).SingleAsync()) == primary,
            "A previously verified tank relationship survives absent legacy hints");
        foreach (var (id, rfid, expected) in new[] { (230001L, "RFID-VERIFIED-ONLY", (Guid?)rfidOnly), (230002L, "RFID-VERIFIED-AMBIG", (Guid?)null) })
        {
            var dto = Packet(id, null); dto.TankSessions[0].TankSerial = null; dto.TankSessions[0].TankLegacyId = null; dto.TankSessions[0].TankRfid = rfid;
            var count = await host.DbAsync(c => c.LegacySyncMappings.CountAsync());
            await host.ExpectAsync(dto, HttpStatusCode.OK, suite, "Legacy RFID resolves only an unambiguous existing verified snapshot identity", [key]);
            suite.Check(await host.DbAsync(c => c.TankSessions.Where(x => x.LegacyId == id * 10 + 1).Select(x => x.TankId).SingleAsync()) == expected &&
                await host.DbAsync(c => c.LegacySyncMappings.CountAsync()) == count, "RFID fallback never creates ownership and preserves ambiguity");
        }
        var oldChild = new HistoryTankSessionIngestDto { SourceTankSessionId = 17, TankRfid = "RFID17", Order = 1, HighPressureBefore = "2", HighPressureAfter = "1" };
        const string oldChildHash = "9EECE8D5D72923BDDB40C59FF8B9CBAA460F15C16377E0CC8B0936C93768590D";
        suite.Check(Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(oldChild))) == oldChildHash,
            "Omitted SourceTankId preserves the exact pre-change normalized child payload hash");
        suite.Check(!JsonSerializer.Serialize(oldChild).Contains("SourceTankId", StringComparison.Ordinal), "Legacy null stable tank ID is not added to receipt hash input");
    }

    private static async Task RejectWithoutChanges(CoreHistoryHost host, HistorySessionIngestDto dto, HttpStatusCode status, TestSuite suite, string key, string label)
    {
        var before = await host.FingerprintAsync();
        await host.ExpectAsync(dto, status, suite, label, [key]);
        suite.Check(before == await host.FingerprintAsync(), "Rejected snapshot leaves graph, receipt and audit state unchanged");
    }
}

sealed class CoreHistoryHost(WebApplication app, HttpClient http) : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public WebApplication App { get; } = app;
    public HttpClient Http { get; } = http;
    public Uri Address => Http.BaseAddress!;

    public static async Task<CoreHistoryHost> StartAsync(string connection, string key, string jwtSecret)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production, ApplicationName = typeof(IntegrationHistoryController).Assembly.GetName().Name });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection, ["Jwt:Issuer"] = "history-tests", ["Jwt:Audience"] = "history-tests",
            ["Jwt:Secret"] = jwtSecret, ["Jwt:ExpirationMinutes"] = "5", ["Jwt:RememberMeDays"] = "1", ["ApiKey:Key"] = key
        });
        builder.Logging.ClearProviders(); builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddApplicationServices(builder.Configuration).AddBusinessServices().AddDalServices(builder.Configuration);
        builder.Services.AddSingleton<IPermissionService>(AllowTestEmployee.Create());
        builder.Services.AddControllers().ConfigureApplicationPartManager(parts => { parts.ApplicationParts.Clear(); parts.ApplicationParts.Add(new AssemblyPart(typeof(IntegrationHistoryController).Assembly)); });
        var app = builder.Build(); app.UseExceptionHandler(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new CoreHistoryHost(app, new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(30) });
    }

    public async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> operation)
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await operation(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public async Task<string> FingerprintAsync() => await DbAsync(async context =>
    {
        var parents = await context.Sessions.IgnoreQueryFilters().AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.LegacyId, row.EngineId, row.SourceRevision, row.StartTime, row.EndTime, row.Mode, row.UserCode, row.IsDeleted }).ToListAsync();
        var children = await context.TankSessions.IgnoreQueryFilters().AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.LegacyId, row.SessionId, row.TankId, row.TankRfid, row.PressureBefore, row.PressureAfter, row.WeightBefore, row.WeightAfter, row.IsDeleted }).ToListAsync();
        var receipts = await context.HistoryIngestReceipts.AsNoTracking().OrderBy(row => row.EventId).ToListAsync();
        var logs = await context.DataChangeLogs.CountAsync();
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { parents, children, receipts, logs })));
    });

    public async Task<HttpResponseMessage> PostRawAsync(string body, string[]? keys = null, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/integration/history/sessions") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (keys is not null) request.Headers.TryAddWithoutValidation(ApiKeyOptions.HeaderName, keys);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await Http.SendAsync(request);
    }

    public async Task<HistoryIngestResultDto?> ExpectAsync(HistorySessionIngestDto dto, HttpStatusCode status, TestSuite suite, string label, string[]? keys = null, string? bearer = null)
    {
        var wire = JsonSerializer.SerializeToNode(dto, Json)!;
        // The production converter normalizes output to UTC. Invalid-input cases must retain the supplied wire offset.
        if (dto.StartTime.Offset != TimeSpan.Zero) wire["startTime"] = dto.StartTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        if (dto.EndTime is { } end && end.Offset != TimeSpan.Zero) wire["endTime"] = end.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        using var response = await PostRawAsync(wire.ToJsonString(), keys, bearer);
        suite.Check(response.StatusCode == status, label + $" (expected {(int)status}, received {(int)response.StatusCode})");
        if (status != HttpStatusCode.OK) return null;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Deserialize<HistoryIngestResultDto>(Json)!;
    }

    public string Jwt(string secret) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("history-tests", "history-tests",
        [new Claim(AppClaimTypes.UserType, "Employee"), new Claim(AppClaimTypes.EmployeeId, "44444444-4444-4444-8444-444444444444"), new Claim(AppClaimTypes.UserId, Guid.NewGuid().ToString()), new Claim(AppClaimTypes.Username, "synthetic-employee")],
        expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256)));

    public async ValueTask DisposeAsync() { Http.Dispose(); await App.StopAsync(); await App.DisposeAsync(); }
}

public class AllowTestEmployee : DispatchProxy
{
    public static IPermissionService Create() => Create<IPermissionService, AllowTestEmployee>();
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
    {
        nameof(IPermissionService.HasPermissionAsync) => Task.FromResult(true), nameof(IPermissionService.IsSuperAdminAsync) => Task.FromResult(false),
        _ => throw new TestFailure("Unexpected permission service call in the synthetic test host.")
    };
}

sealed class TestSuite
{
    private int count;
    public int Count => count;
    public void Check(bool passed, string label) { if (!passed) throw new TestFailure(label); Interlocked.Increment(ref count); Console.WriteLine("PASS: " + label); }
    public async Task ThrowsAsync<T>(Func<Task> operation, string label) where T : Exception
    {
        try { await operation(); } catch (T) { Check(true, label); return; }
        throw new TestFailure(label);
    }
}
sealed class TestFailure(string message) : Exception(message);

sealed class DisposablePostgres(string adminConnection, string connection, string databaseName) : IAsyncDisposable
{
    public string ConnectionString { get; } = connection;
    public static async Task<DisposablePostgres> CreateAsync()
    {
        var options = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TEST_PG_CONNECTION") ?? "Host=127.0.0.1;Port=5432;Username=kienkien;Database=postgres");
        if (!(options.Host == "127.0.0.1" || options.Host == "localhost" || options.Host == "::1")) throw new TestFailure("TEST_PG_CONNECTION must point to a local PostgreSQL test server.");
        options.Database = "postgres"; options.Pooling = false; options.Timeout = 10; options.CommandTimeout = 30;
        var adminConnection = options.ConnectionString;
        var name = "core_history_tests_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin); await command.ExecuteNonQueryAsync();
        options.Database = name;
        return new DisposablePostgres(adminConnection, options.ConnectionString, name);
    }

    public async ValueTask DisposeAsync()
    {
        if (!databaseName.StartsWith("core_history_tests_", StringComparison.Ordinal) || databaseName.Length != 51)
            throw new TestFailure("Disposable database ownership check failed; refusing cleanup.");
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin); await command.ExecuteNonQueryAsync();
        Console.WriteLine("PASS: disposable history test database removed.");
    }
}
