using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CorePlasma.API.SourceBusiness;
using CorePlasma.API.SourceSync;
using CorePlasma.DAL.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CorePlasma.API.SourceProjection;

public static class SourceProjectionRegistration
{
    public static IServiceCollection AddSourceProjectionServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SourceProjectionOptions>(configuration.GetSection("SourceProjection"));
        services.AddScoped<NativeProjectionStore>();
        services.AddScoped<NativeProfileProjector>();
        services.AddScoped<NativeBusinessProjector>();
        services.AddScoped<NativeTankEngineHistoryProjector>();
        services.AddScoped<SourceProjectionRunner>();
        services.AddHostedService<SourceProjectionWorker>();
        return services;
    }
}

public sealed class SourceProjectionWorker(IServiceScopeFactory scopes, IOptionsMonitor<SourceProjectionOptions> options,
    ILogger<SourceProjectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.Enabled)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<SourceProjectionRunner>().RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception error) { logger.LogError("Source native projection paused: {ErrorCode} ({ErrorType})", NativeProjectionStore.ErrorCode(error), error.GetType().Name); }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.PollSeconds, 5, 3600)), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

public sealed class SourceProjectionRunner(ApplicationDbContext db, ISourceSyncRawReader raw, NativeProjectionStore store,
    NativeProfileProjector profiles, NativeBusinessProjector business, NativeTankEngineHistoryProjector history,
    IServiceProvider services, IOptionsMonitor<SourceProjectionOptions> options, ILogger<SourceProjectionRunner> logger)
{
    public async Task RunOnceAsync(CancellationToken ct)
    {
        var settings = options.CurrentValue;
        if (!settings.Enabled) return;
        if (!db.Database.IsSqlServer()) throw new SourceProjectionException("native_projection_requires_sql_server");
        if (settings.Sources.Length == 0 || settings.Sources.Any(source => source is not ("quanly" or "newapp")) || settings.Sources.Distinct().Count() != settings.Sources.Length)
            throw new SourceProjectionException("invalid_projection_source_configuration");
        if (string.IsNullOrEmpty(settings.PolicyVersion) || settings.PolicyVersion.Length > 40)
            throw new SourceProjectionException("invalid_projection_policy_version");
        // A SESSION lock spans the many small native transactions. Only one bridge replica owns these entity streams.
        await db.Database.OpenConnectionAsync(ct);
        var connection = db.Database.GetDbConnection();
        var locked = false;
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "DECLARE @r int; EXEC @r=sp_getapplock @Resource=N'CorePlasma:raw-native:v1',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; SELECT @r;";
                locked = Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) >= 0;
            }
            if (!locked) return;
            foreach (var source in settings.Sources) await ProcessSourceAsync(source, settings, ct);
        }
        finally
        {
            if (locked && connection.State == ConnectionState.Open)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "EXEC sp_releaseapplock @Resource=N'CorePlasma:raw-native:v1',@LockOwner='Session';";
                await command.ExecuteNonQueryAsync(CancellationToken.None);
            }
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task ProcessSourceAsync(string source, SourceProjectionOptions settings, CancellationToken ct)
    {
        var state = await raw.GetStateAsync(source, ct);
        if (state?.Status != "ready") return;
        // Fetch receipts BEFORE the coherent raw snapshot: every selected receipt is reflected in that snapshot.
        var pending = await raw.ReadPendingAsync(source, Math.Clamp(settings.EventBatchSize, 1, 1000), ct);
        var policyHash = SourceProjectionIdentity.BaselineHash(state.Generation, settings);
        var baselineMarker = await store.FindAsync(source, state.InstallationId, state.Generation, "_profile_baseline", 1, ct);
        var baselineRequired = baselineMarker?.SnapshotHash != policyHash;
        var historyCheckpoint = source == "newapp" && settings.TankEngineHistoryEnabled
            ? await store.CheckpointAsync(source, state.InstallationId, state.Generation, "_history_baseline", ct) : null;
        var historyPending = source == "newapp" && settings.TankEngineHistoryEnabled &&
            (historyCheckpoint?.SnapshotHash != policyHash || historyCheckpoint?.ErrorCode == "history_baseline_in_progress");
        // A completed scan may contain protected/unresolved records; these remain visible per identity.
        // Reconcile an administrator's ownership fix by bumping PolicyVersion, rather than re-reading
        // every order and tank on an idle 30-second timer.
        if (!baselineRequired && !historyPending && pending.Count == 0) return;
        var generic = services.GetService<ISourceBusinessProjector>();
        if (!baselineRequired && !historyPending && !pending.Any(change => SourceSnapshotBuilder.Entity(source, change.Event.Table) is not null || SourceSnapshotBuilder.IsSupport(source, change.Event.Table)))
        {
            foreach (var change in pending)
            {
                var result = await GenericEventAsync(generic, change, ct);
                if (result.State == "unsupported") result = result with { ErrorCode = SourceSnapshotBuilder.IsDirect(source, change.Event.Table) ? "owned_by_existing_direct_stream" : result.ErrorCode ?? "no_native_business_adapter" };
                await raw.SetProjectionStateAsync(source, change.InstallationId, change.EventId, result.State, result.ErrorCode, ct);
            }
            return;
        }
        var registered = await raw.GetTablesAsync(source, ct);
        var registeredNames = registered.Select(table => table.Name).ToHashSet(StringComparer.Ordinal);
        var onlyHistoryCatalog = !baselineRequired && !pending.Any(change =>
            SourceSnapshotBuilder.Entity(source, change.Event.Table) is { } entity && entity != "tank-engine-history" || SourceSnapshotBuilder.IsSupport(source, change.Event.Table));
        // History resolves engines through verified SQL mappings; only the source
        // Tank RFID index is required. Do not reload every order/support table.
        var selected = SourceSnapshotBuilder.Tables(source).Where(table => registeredNames.Contains(table) && table != "TankEngineHistory"
            && (!onlyHistoryCatalog || table == "Tank")).ToArray();
        if (selected.Length == 0) throw new SourceProjectionException("native_source_tables_missing");
        var snapshot = await raw.ReadProjectionSnapshotAsync(source, selected, ct);
        var catalog = new SourceProjectionCatalog(snapshot);
        var targets = BuildTargets(source, pending);
        var progress = (await store.ReadProfilesAsync(source, state.InstallationId, state.Generation, ct)).ToDictionary(entry => (entry.Entity, entry.EntityId));
        var revision = pending.FirstOrDefault()?.EventId ?? "baseline";
        var occurred = pending.FirstOrDefault() is { } first ? NativeBusinessProjector.ParseDate(first.Event.OccurredAt) : DateTime.UnixEpoch;
        var allResults = new List<NativeProjectionState>();
        // Customers/staff precede assets and business FKs. Raw state may be coalesced, never raw sequence IDs.
        foreach (var entity in new[] { "customer", "user", "engine", "tank", "order", "transfer" })
        {
            foreach (var table in selected.Where(table => SourceSnapshotBuilder.Entity(source, table) == entity))
            {
                foreach (var row in catalog.Rows(table))
                {
                    long id;
                    try { id = SourceSnapshotBuilder.Id(source, row.Values); }
                    catch (SourceProjectionException)
                    {
                        allResults.Add(await ProjectRowAsync(source, state, revision, entity, row, catalog, false,
                            baselineRequired ? "create" : "update", new DateTimeOffset(occurred, TimeSpan.Zero), progress, settings, ct));
                        continue;
                    }
                    if (!baselineRequired && (!targets.TryGetValue(entity, out var target) || target is not null && !target.Contains(id))) continue;
                    var inserted = pending.Any(change => change.Event.Table == table && change.Event.Operation == "INSERT" &&
                        RowId(source, change.Event.NewRow) == id);
                    var result = await ProjectRowAsync(source, state, revision, entity, row, catalog, false,
                        baselineRequired || inserted ? "create" : "update", new DateTimeOffset(occurred, TimeSpan.Zero), progress, settings, ct);
                    allResults.Add(result);
                }
            }
        }

        NativeProjectionState? historyResult = null;
        if (source == "newapp" && settings.TankEngineHistoryEnabled)
        {
            var historyTable = registered.SingleOrDefault(table => table.Name == "TankEngineHistory")
                ?? throw new SourceProjectionException("native_history_table_missing");
            historyResult = await BaselineHistoryAsync(state, catalog, historyTable, policyHash, settings, ct);
            allResults.Add(historyResult);
            progress[(historyResult.Entity, historyResult.EntityId)] = historyResult;
        }

        var genericBaseline = new BusinessProjectionResult("blocked", "business_projector_not_registered");
        if (generic is not null)
        {
            try { genericBaseline = await generic.ProjectBaselineAsync(source, state.InstallationId, state.Generation, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) { db.ChangeTracker.Clear(); genericBaseline = new("blocked", NativeProjectionStore.ErrorCode(error)); }
        }

        foreach (var change in pending)
        {
            var entity = SourceSnapshotBuilder.Entity(source, change.Event.Table);
            var results = new List<NativeProjectionState>();
            if (entity is not null)
            {
                var ids = new[] { RowId(source, change.Event.OldRow), RowId(source, change.Event.NewRow) }.Where(id => id is > 0).Select(id => id!.Value).Distinct().ToArray();
                if (change.Event.Operation == "TRUNCATE")
                {
                    // Include durable ownership committed with native rows, even
                    // if a crash prevented the later metadata save. Current raw
                    // presence still wins over an older TRUNCATE receipt below.
                    ids = await store.ReadOwnedEntityIdsAsync(source, state.InstallationId, state.Generation, entity, ct);
                }
                else if (ids.Length == 0)
                {
                    var invalid = new NativeProjectionState(entity, 0, null, "", "blocked", "invalid_source_identity");
                    await store.SaveAsync(source, state.InstallationId, state.Generation, invalid, change.EventId, ct);
                    results.Add(invalid);
                }
                foreach (var id in ids)
                {
                    SourceSyncRawRow? current;
                    try
                    {
                        current = entity == "tank-engine-history"
                            ? await raw.ReadRowAsync(source, change.Event.Table, new Dictionary<string, string?> { [source == "quanly" ? "id" : "Id"] = id.ToString(CultureInfo.InvariantCulture) }, ct)
                            : catalog.Find(change.Event.Table, id, source == "quanly" ? "id" : "Id");
                    }
                    catch (SourceProjectionException error)
                    {
                        var duplicate = new NativeProjectionState(entity, id, progress.GetValueOrDefault((entity, id))?.CoreId, "", "blocked", NativeProjectionStore.ErrorCode(error));
                        await store.SaveAsync(source, state.InstallationId, state.Generation, duplicate, change.EventId, ct);
                        progress[(entity, id)] = duplicate; results.Add(duplicate); continue;
                    }
                    if (current is not null)
                    {
                        results.Add(await ProjectRowAsync(source, state, change.EventId, entity, current, catalog, false,
                            change.Event.Operation == "INSERT" ? "create" : "update", NativeBusinessProjector.ParseDate(change.Event.OccurredAt), progress, settings, ct));
                    }
                    else
                    {
                        var previous = RowId(source, change.Event.OldRow) == id ? change.Event.OldRow : change.Event.NewRow;
                        var old = previous.ValueKind == JsonValueKind.Object ? ToRow(change.Event.Table, previous)
                            : new SourceSyncRawRow(change.Event.Table, new Dictionary<string, string?> { [source == "quanly" ? "id" : "Id"] = id.ToString(CultureInfo.InvariantCulture) });
                        results.Add(await ProjectRowAsync(source, state, change.EventId, entity, old, catalog, true, "delete",
                            NativeBusinessProjector.ParseDate(change.Event.OccurredAt), progress, settings, ct));
                    }
                }
            }
            else if (SourceSnapshotBuilder.IsSupport(source, change.Event.Table)) results.AddRange(allResults.Where(result => result.Entity is "tank" or "order" or "transfer"));
            var genericResult = await GenericEventAsync(generic, change, ct);
            var bad = results.FirstOrDefault(result => result.State != "applied");
            var nativeHandled = entity is not null || SourceSnapshotBuilder.IsSupport(source, change.Event.Table);
            // Direct streams have independent durable receipts; transport delivery is not proof that stream applied.
            var final = bad is not null ? new BusinessProjectionResult("blocked", bad.ErrorCode ?? "native_projection_blocked")
                : genericResult.State is "blocked" ? genericResult
                : nativeHandled || genericResult.State == "applied" ? new BusinessProjectionResult("applied")
                : new BusinessProjectionResult("unsupported", SourceSnapshotBuilder.IsDirect(source, change.Event.Table) ? "owned_by_existing_direct_stream" : genericResult.ErrorCode ?? "no_native_business_adapter");
            await raw.SetProjectionStateAsync(source, change.InstallationId, change.EventId, final.State, final.ErrorCode, ct);
        }
        if (historyResult is not null && historyResult.ErrorCode != "history_baseline_in_progress" &&
            pending.Any(change => change.Event.Table == "TankEngineHistory"))
        {
            var refreshed = await CompletedHistoryAsync(state, policyHash, ct);
            allResults.Remove(historyResult); allResults.Add(refreshed);
            historyResult = refreshed;
            progress[(refreshed.Entity, refreshed.EntityId)] = refreshed;
        }
        var blocked = allResults.Count(result => result.State != "applied");
        if (baselineRequired) await store.SaveAsync(source, state.InstallationId, state.Generation,
            new("_profile_baseline", 1, null, policyHash, "applied", blocked == 0 ? null : "native_baseline_has_unresolved_records"), "baseline", ct);
        // Include previously unresolved records, not just this event batch's affected identities.
        var unresolved = progress.Values.Any(result => result.Entity != "_profile_baseline" && result.State != "applied");
        var baselineOk = !unresolved && blocked == 0 && genericBaseline.State == "applied";
        await raw.SetBaselineProjectionStateAsync(source, state.Generation, baselineOk ? "applied" : "blocked",
            baselineOk ? null : historyResult?.ErrorCode == "history_baseline_in_progress" ? "history_baseline_in_progress"
                : blocked > 0 ? "native_baseline_has_unresolved_records" : genericBaseline.ErrorCode ?? "business_baseline_blocked", ct);
        logger.LogInformation("Source native projection {Source}: {Records} records, {Blocked} unresolved, {Events} receipts checked", source, allResults.Count, blocked, pending.Count);
    }

    private async Task<NativeProjectionState> ProjectRowAsync(string source, SourceSyncState state, string revision, string entity,
        SourceSyncRawRow row, SourceProjectionCatalog catalog, bool deleting, string operation, DateTimeOffset occurredAt,
        Dictionary<(string Entity, long EntityId), NativeProjectionState> progress, SourceProjectionOptions settings, CancellationToken ct)
    {
        long id = 0;
        var hash = "";
        NativeProjectionState result;
        try
        {
            id = SourceSnapshotBuilder.Id(source, row.Values);
            if (id <= 0) throw new SourceProjectionException("invalid_source_identity");
            if (!deleting && entity != "tank-engine-history") catalog.Find(row.Table, id, source == "quanly" ? "id" : "Id");
            JsonElement snapshot = entity is "order" or "transfer" ? business.Snapshot(source, entity, row, catalog, deleting)
                : entity == "tank-engine-history" ? JsonSerializer.SerializeToElement(new { row = row.Values, deleted = deleting })
                : SourceSnapshotBuilder.Build(source, entity, row, catalog, deleting);
            hash = SourceProjectionIdentity.RecordHash(settings.PolicyVersion, snapshot);
            if (progress.TryGetValue((entity, id), out var old) && old.SnapshotHash == hash && old.State is "applied" or "protected") return old;
            if (entity == "order" && !settings.OrdersEnabled || entity == "transfer" && !settings.TransfersEnabled ||
                entity == "tank-engine-history" && !settings.TankEngineHistoryEnabled) throw new SourceProjectionException("native_adapter_disabled");
            result = entity switch
            {
                "order" or "transfer" => await business.ApplyAsync(source, state.InstallationId, entity, row, catalog, snapshot, deleting, ct),
                "tank-engine-history" => await history.ApplyAsync(row, catalog, deleting, ct),
                _ => await profiles.ApplyAsync(source, state.InstallationId, state.Generation, revision + "-" + settings.PolicyVersion, entity, id, snapshot,
                    snapshot.TryGetProperty("deleted_at", out var deleted) && deleted.ValueKind != JsonValueKind.Null ? "delete" : operation, occurredAt, ct)
            };
            result = result with { SnapshotHash = hash };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) { result = new(entity, id, progress.GetValueOrDefault((entity, id))?.CoreId, hash, "blocked", NativeProjectionStore.ErrorCode(error)); }
        db.ChangeTracker.Clear();
        await store.SaveAsync(source, state.InstallationId, state.Generation, result, revision, ct);
        progress[(entity, id)] = result;
        return result;
    }

    private async Task<NativeProjectionState> BaselineHistoryAsync(SourceSyncState state, SourceProjectionCatalog catalog,
        SourceSyncTable table, string policyHash, SourceProjectionOptions settings, CancellationToken ct)
    {
        var marker = await store.CheckpointAsync("newapp", state.InstallationId, state.Generation, "_history_baseline", ct);
        if (marker?.SnapshotHash == policyHash && marker.ErrorCode != "history_baseline_in_progress")
            return new("_history_baseline", 1, null, policyHash, marker.State, marker.ErrorCode);
        if (table.PrimaryKey.Length != 1 || table.PrimaryKey[0] != "Id")
            throw new SourceProjectionException("history_primary_key_contract_missing");
        var cursor = marker?.SnapshotHash == policyHash && marker.Revision.Length > 0 ? marker.Revision : null;
        var watch = Stopwatch.StartNew();
        var budgetRows = Math.Clamp(settings.HistoryBaselineMaxRowsPerPoll, 1, 10000);
        var budgetTime = TimeSpan.FromSeconds(Math.Clamp(settings.HistoryBaselineMaxSeconds, 1, 300));
        var processed = 0;
        while (processed < budgetRows && watch.Elapsed < budgetTime)
        {
            var page = await raw.ReadPageAsync("newapp", table.Name, cursor, Math.Min(200, budgetRows - processed), null, null, null, ct);
            if (page.Rows.Count == 0) return await CompletedHistoryAsync(state, policyHash, ct);
            var completedPage = 0;
            foreach (var row in page.Rows)
            {
                ct.ThrowIfCancellationRequested();
                var key = table.PrimaryKey.Select(column => row.Values.TryGetValue(column, out var value) && value is not null
                    ? value : throw new SourceProjectionException("history_primary_key_value_missing"));
                var nextCursor = Convert.ToBase64String(SourceSyncCodec.Hash(key));
                var cache = new Dictionary<(string, long), NativeProjectionState>();
                try
                {
                    var id = SourceSnapshotBuilder.Id("newapp", row.Values);
                    var old = await store.FindAsync("newapp", state.InstallationId, state.Generation, "tank-engine-history", id, ct);
                    if (old is not null) cache[("tank-engine-history", id)] = old;
                }
                catch (SourceProjectionException) { /* ProjectRow records the invalid identity durably. */ }
                await ProjectRowAsync("newapp", state, "baseline", "tank-engine-history", row, catalog, false,
                    "update", DateTimeOffset.UnixEpoch, cache, settings, ct);
                // Advance only after the row's native outcome is durable. On a
                // crash before checkpoint save the page replays idempotently.
                cursor = nextCursor; processed++; completedPage++;
                if (processed >= budgetRows || watch.Elapsed >= budgetTime) break;
            }
            if (completedPage == page.Rows.Count && page.NextCursor is null)
                return await CompletedHistoryAsync(state, policyHash, ct);
            if (completedPage < page.Rows.Count) break;
            if (page.NextCursor != cursor) throw new SourceProjectionException("history_page_cursor_mismatch");
        }
        var inProgress = new NativeProjectionState("_history_baseline", 1, null, policyHash, "blocked", "history_baseline_in_progress");
        await store.SaveAsync("newapp", state.InstallationId, state.Generation, inProgress, cursor ?? "", ct);
        return inProgress;
    }

    private async Task<NativeProjectionState> CompletedHistoryAsync(SourceSyncState state, string policyHash, CancellationToken ct)
    {
        var unresolved = await store.HasUnresolvedAsync("newapp", state.InstallationId, state.Generation, "tank-engine-history", ct);
        var result = new NativeProjectionState("_history_baseline", 1, null, policyHash, unresolved ? "blocked" : "applied",
            unresolved ? "history_baseline_has_unresolved_records" : null);
        await store.SaveAsync("newapp", state.InstallationId, state.Generation, result, "", ct);
        return result;
    }

    private static long? RowId(string source, JsonElement row) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(source == "quanly" ? "id" : "Id", out var value)
        && value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
    private static SourceSyncRawRow ToRow(string table, JsonElement row) => new(table, row.EnumerateObject().ToDictionary(property => property.Name,
        property => property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString(), StringComparer.Ordinal));

    private async Task<BusinessProjectionResult> GenericEventAsync(ISourceBusinessProjector? generic, SourceSyncPendingEvent change, CancellationToken ct)
    {
        if (generic is null) return new("unsupported", "no_business_adapter");
        try { return await generic.ProjectEventAsync(change, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) { db.ChangeTracker.Clear(); return new("blocked", NativeProjectionStore.ErrorCode(error)); }
    }

    private static Dictionary<string, HashSet<long>?> BuildTargets(string source, IReadOnlyList<SourceSyncPendingEvent> pending)
    {
        var targets = new Dictionary<string, HashSet<long>?>(StringComparer.Ordinal);
        void All(string entity) => targets[entity] = null;
        void Add(string entity, long? id)
        {
            if (id is not > 0) return;
            if (!targets.TryGetValue(entity, out var ids)) targets[entity] = ids = [];
            else if (ids is null) return;
            ids.Add(id.Value);
        }
        foreach (var change in pending)
        {
            var table = change.Event.Table;
            var entity = SourceSnapshotBuilder.Entity(source, table);
            if (entity is not null)
            {
                if (change.Event.Operation == "TRUNCATE") All(entity);
                else { Add(entity, RowId(source, change.Event.OldRow)); Add(entity, RowId(source, change.Event.NewRow)); }
                continue;
            }
            foreach (var image in new[] { change.Event.OldRow, change.Event.NewRow })
            {
                if (image.ValueKind != JsonValueKind.Object) continue;
                long? Field(string field) => image.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String &&
                    long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
                if (source == "quanly" && table is "order_items" or "order_tank_items" or "order_shippings") Add("order", Field("order_id"));
                if (source == "quanly" && table is "move_items" or "move_tank_items" or "move_tank_shippings") Add("transfer", Field("move_tank_id"));
                if (source == "newapp" && table is "OrderDetail" or "OrderShipping" or "OrderWarehouse" or "CustomerTank") Add("order", Field("OrderId"));
                if (source == "newapp" && table is "CustomerTank" or "WarehouseTank") Add("tank", Field("TankId"));
                if (source == "newapp" && table == "MoveWarehouseOrderTank") Add("transfer", Field("MoveWarehouseOrderId"));
            }
            if ((source, table) is ("quanly", "capacities" or "warehouses" or "factories") or ("newapp", "Capacity" or "Warehouse" or "TankCycle") ||
                change.Event.Operation == "TRUNCATE" && SourceSnapshotBuilder.IsSupport(source, table))
            { All("tank"); All("order"); All("transfer"); }
        }
        return targets;
    }
}
