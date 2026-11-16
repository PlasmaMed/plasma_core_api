using System.Collections;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SourceContext = PlasmaVN.Infrastructure.PlasmaContext;
using SourceEntity = PlasmaVN.Infrastructure.Entities;

// Exercises the actual administrative entry point against the already-owned disposable source database.
static class BackfillCases
{
    private const long FirstId = 7301;
    private const long SecondId = 7302;
    private const long UnqueuedId = 7303;
    private const long MissingId = 99990000;
    private static readonly string[] DefaultConnectionOverrides =
    [
        "ConnectionStrings__DefaultConnection", "CUSTOMCONNSTR_DefaultConnection",
        "SQLCONNSTR_DefaultConnection", "SQLAZURECONNSTR_DefaultConnection", "MYSQLCONNSTR_DefaultConnection"
    ];

    private static SourceContext Context(string connection) =>
        new(new DbContextOptionsBuilder<SourceContext>().UseNpgsql(connection).Options);

    public static async Task RunAsync(string connection, TestSuite suite)
    {
        var database = new NpgsqlConnectionStringBuilder(connection);
        if (!(database.Host is "127.0.0.1" or "localhost" or "::1") ||
            !database.Database.StartsWith("core_history_tests_", StringComparison.Ordinal) || database.Database.Length != 51)
            throw new TestFailure("Backfill CLI tests require the owned local disposable database.");

        var directory = Path.Combine(Path.GetTempPath(), "history_backfill_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configFile = Path.Combine(directory, "synthetic-config.json");
        var idsFile = Path.Combine(directory, "synthetic-ids.txt");
        // The production CLI permits environment configuration overrides. Pin every spelling of this
        // key to the disposable database for these sequential calls, then restore the calling process.
        var environment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Where(entry => IsDefaultConnectionOverride((string)entry.Key))
            .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value);
        foreach (var key in DefaultConnectionOverrides) environment.TryAdd(key, null);
        try
        {
            foreach (var key in environment.Keys) Environment.SetEnvironmentVariable(key, connection);
            await File.WriteAllTextAsync(configFile, JsonSerializer.Serialize(new
            {
                ConnectionStrings = new { DefaultConnection = connection }
            }));
            await using (var context = Context(connection))
            {
                context.Sessions.AddRange(Session(FirstId), Session(SecondId), Session(UnqueuedId));
                await context.SaveChangesAsync();
            }
            await File.WriteAllTextAsync(idsFile, $"{FirstId}\n{SecondId}\n{MissingId}\n");
            var baseline = await StateAsync(connection);

            suite.Check(await RunAsync(idsFile, configFile) == 0,
                "Actual backfill CLI accepts a read-only selection including a missing source ID");
            suite.Check(await StateAsync(connection) == baseline,
                "Backfill dry run creates no rows and does not allocate an outbox sequence value");

            suite.Check(await RunAsync(idsFile, configFile, "--apply") == 0,
                "Actual backfill CLI captures persisted source history in bounded transactions");
            await using (var context = Context(connection))
            {
                var captured = await context.CoreHistoryOutbox.AsNoTracking()
                    .Where(row => row.SourceSessionId == FirstId || row.SourceSessionId == SecondId)
                    .OrderBy(row => row.SourceSessionId).ToListAsync();
                suite.Check(captured.Count == 2 && captured.Select(row => row.SourceSessionId).SequenceEqual([FirstId, SecondId]),
                    "Backfill queues exactly the requested source rows that exist");
                suite.Check(!await context.CoreHistoryOutbox.AnyAsync(row => row.SourceSessionId == MissingId) &&
                            !await context.Sessions.AnyAsync(row => row.Id == MissingId),
                    "Missing backfill IDs never create a phantom source session or queue snapshot");
                foreach (var capturedRow in captured)
                {
                    using var document = JsonDocument.Parse(capturedRow.PayloadJson);
                    var payload = document.RootElement;
                    var persisted = Session(capturedRow.SourceSessionId);
                    suite.Check(payload.GetProperty("sourceSessionId").GetInt64() == persisted.Id &&
                                payload.GetProperty("sourceRevision").GetInt64() == capturedRow.Id &&
                                payload.GetProperty("eventId").GetGuid() == capturedRow.EventId &&
                                payload.GetProperty("engineSerial").GetString() == "SYNTHETIC-ENGINE-A" &&
                                payload.GetProperty("startTime").GetDateTime() == DateTime.SpecifyKind(persisted.StartTime, DateTimeKind.Utc) &&
                                payload.GetProperty("endTime").GetDateTime() == DateTime.SpecifyKind(persisted.EndTime, DateTimeKind.Utc) &&
                                payload.GetProperty("startTime").GetDateTime().Kind == DateTimeKind.Utc &&
                                payload.GetProperty("userCode").GetString() == persisted.UserCode &&
                                payload.GetProperty("mode").GetString() == persisted.Mode &&
                                payload.GetProperty("averageSpeed").GetString() == persisted.AverageSpeed &&
                                payload.GetProperty("plasmaOnTime").GetString() == persisted.PlasmaOnTime &&
                                payload.GetProperty("environmentTemperatureBefore").GetString() == persisted.EnviromentTemperatureBefore &&
                                payload.GetProperty("handleTemperatureAfter").GetString() == persisted.HandleTemperatureAfter &&
                                payload.GetProperty("errorCode").GetString() == persisted.ErrorCode,
                        "Backfill uses the canonical persisted source snapshot with unchanged UTC times and user code");
                    var tanks = payload.GetProperty("tankSessions");
                    suite.Check(tanks.GetArrayLength() == 1 &&
                                tanks[0].GetProperty("sourceTankSessionId").GetInt64() == persisted.TankSessions[0].Id &&
                                tanks[0].GetProperty("tankRfid").GetString() == persisted.TankSessions[0].RFID &&
                                tanks[0].GetProperty("tankSerial").GetString() == "SYNTHETIC-TANK-A" &&
                                tanks[0].GetProperty("tankLegacyId").GetString() == "synthetic-legacy-tank-a" &&
                                tanks[0].GetProperty("order").GetInt32() == 1 &&
                                tanks[0].GetProperty("highPressureBefore").GetString() == "11250" &&
                                tanks[0].GetProperty("highPressureAfter").GetString() == "12500" &&
                                tanks[0].GetProperty("loadCellBefore").GetString() == "39.25" &&
                                tanks[0].GetProperty("loadCellAfter").GetString() == "40.50",
                        "Backfill preserves source child identity, tank hints and raw Before/After measurements");
                }
            }
            var capturedState = await StateAsync(connection);
            suite.Check(capturedState.Sessions == baseline.Sessions && capturedState.TankSessions == baseline.TankSessions &&
                        capturedState.SourceFingerprint == baseline.SourceFingerprint,
                "Backfill capture does not insert or alter the source history graph");
            suite.Check(await RunAsync(idsFile, configFile, "--apply") == 0 && await StateAsync(connection) == capturedState,
                "Rerunning committed backfill skips queued IDs without new rows or sequence allocations");

            foreach (var malformed in new[]
            {
                // Start with a real unqueued row: an incremental parser would wrongly commit it before seeing the bad line.
                $"{UnqueuedId}\n{UnqueuedId}\n", $"{UnqueuedId}\n{FirstId}\n", $"{UnqueuedId}\nnot-an-id\n", $"{UnqueuedId}\n0\n"
            })
            {
                await File.WriteAllTextAsync(idsFile, malformed);
                suite.Check(await RunAsync(idsFile, configFile, "--apply") == 2 && await StateAsync(connection) == capturedState,
                    "Malformed backfill ID files are rejected in full before any source or queue mutation");
            }
            await File.WriteAllTextAsync(idsFile, $"{UnqueuedId}\n");
            suite.Check(await RunAsync(idsFile, configFile, "--dispatch") == 2 && await StateAsync(connection) == capturedState,
                "Backfill refuses dispatch without explicit apply before touching the queue or peer");
        }
        finally
        {
            foreach (var entry in environment) Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task<int> RunAsync(string idsFile, string configFile, params string[] flags) =>
        HistoryBackfill.Program.Main(["--ids-file", idsFile, "--configfile", configFile, "--batch-size", "1", .. flags]);

    private static bool IsDefaultConnectionOverride(string key) =>
        key.Replace("__", ":", StringComparison.Ordinal).Equals("ConnectionStrings:DefaultConnection", StringComparison.OrdinalIgnoreCase) ||
        DefaultConnectionOverrides.Any(name => name.Equals(key, StringComparison.OrdinalIgnoreCase));

    private static SourceEntity.Session Session(long id) => new()
    {
        Id = id, EngineId = 7001,
        StartTime = new DateTime(2026, 10, 12, 1, 2, 3, DateTimeKind.Unspecified),
        EndTime = new DateTime(2026, 10, 12, 1, 12, 3, DateTimeKind.Unspecified),
        UserCode = "synthetic-backfill-user", Mode = "synthetic-backfill", AverageSpeed = "3.25", PlasmaOnTime = "480",
        EnviromentTemperatureBefore = "23.5", HandleTemperatureAfter = "31.25", ErrorCode = "synthetic-error",
        TankSessions = [new() { Id = id * 10, TankId = 7001, RFID = "SYNTHETIC-TANK-A", Order = 1,
            HighPressureBefore = "11250", HighPressureAfter = "12500", LoadCellBefore = "39.25", LoadCellAfter = "40.50" }]
    };

    private static async Task<State> StateAsync(string connection)
    {
        await using var context = Context(connection);
        var sessions = await context.Sessions.CountAsync();
        var tankSessions = await context.TankSessions.CountAsync();
        // Include durable identity/payload and lease state, so a dry run or rerun cannot silently mutate existing queue rows.
        var queue = JsonSerializer.Serialize(await context.CoreHistoryOutbox.AsNoTracking().OrderBy(row => row.Id).ToListAsync());
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT last_value::text || ':' || is_called::text FROM public.\"CoreHistoryOutboxIdSequence\"";
        var sequence = Convert.ToString(await command.ExecuteScalarAsync())!;
        command.CommandText = "SELECT md5(COALESCE((SELECT jsonb_agg(to_jsonb(s) ORDER BY s.\"Id\")::text FROM public.\"Session\" s), '[]') || "
            + "COALESCE((SELECT jsonb_agg(to_jsonb(t) ORDER BY t.\"Id\")::text FROM public.\"TankSession\" t), '[]'))";
        var sourceFingerprint = Convert.ToString(await command.ExecuteScalarAsync())!;
        return new(sessions, tankSessions, queue, sequence, sourceFingerprint);
    }

    private sealed record State(int Sessions, int TankSessions, string Queue, string Sequence, string SourceFingerprint);
}
