using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using PlasmaVN.API.History;
using PlasmaVN.API.Integration;
using SourceContext = PlasmaVN.Infrastructure.PlasmaContext;
using SourceEntity = PlasmaVN.Infrastructure.Entities;

static class SourceCases
{
    private const string PersistedSourceUserCode = "synthetic-persisted-source-user-code";
    private static SourceContext Context(string connection) => new(new DbContextOptionsBuilder<SourceContext>().UseNpgsql(connection).Options);

    public static async Task RunAsync(CoreHistoryHost host, TestSuite suite, string connection, string key)
    {
        await using (var context = Context(connection))
            suite.Check(!await new EfCoreHistoryOutboxStore(context).IsReadyAsync(CancellationToken.None), "Source worker detects a missing outbox without creating schema automatically");

        // Execute the production source migration's actual operations against this test database only.
        await using (var context = Context(connection))
        {
            var migration = new PlasmaVN.Infrastructure.Migrations.AddCoreHistoryOutbox { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" };
            foreach (var command in context.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, context.Model))
                await context.Database.ExecuteSqlRawAsync(command.CommandText);
            suite.Check(await new EfCoreHistoryOutboxStore(context).IsReadyAsync(CancellationToken.None), "Production source outbox migration creates a usable durable queue");
        }

        await using (var graph = new PlasmaVN.Infrastructure.SourceGraphContext(new DbContextOptionsBuilder<PlasmaVN.Infrastructure.SourceGraphContext>().UseNpgsql(connection).Options))
            await graph.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();

        await using (var context = Context(connection))
        {
            context.Engines.Add(new() { Id = 7001, Serial = "SYNTHETIC-ENGINE-A" });
            context.Tanks.Add(new() { Id = 7001, Serial = "SYNTHETIC-TANK-A", OldId = "synthetic-legacy-tank-a" });
            await context.SaveChangesAsync();
            await suite.ThrowsAsync<InvalidOperationException>(() => new CoreHistoryOutboxEnqueuer(context).EnqueueAsync([SourceSession(7100)], CancellationToken.None),
                "Source enqueuer refuses history without a source database transaction");
        }

        await using (var context = Context(connection))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var row = SourceSession(7101); context.Sessions.Add(row); await context.SaveChangesAsync();
            await new CoreHistoryOutboxEnqueuer(context).EnqueueAsync([row], CancellationToken.None); await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using (var context = Context(connection))
            suite.Check(!await context.Sessions.AnyAsync(row => row.Id == 7101) && !await context.CoreHistoryOutbox.AnyAsync(),
                "Rolled-back source save cannot leave an orphan outbox event");

        await using (var context = Context(connection))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var row = SourceSession(7102); context.Sessions.Add(row); await context.SaveChangesAsync();
            await new CoreHistoryOutboxEnqueuer(context).EnqueueAsync([row], CancellationToken.None); await context.SaveChangesAsync();
            await using (var other = Context(connection))
                suite.Check((await new EfCoreHistoryOutboxStore(other).ClaimAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None)).Count == 0,
                    "Background delivery cannot see an uncommitted source outbox event");
            await transaction.CommitAsync();
        }

        CoreHistoryOutboxItem committed;
        await using (var context = Context(connection))
        {
            committed = (await new EfCoreHistoryOutboxStore(context).ClaimAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None)).Single();
            using var payload = JsonDocument.Parse(committed.PayloadJson);
            suite.Check(payload.RootElement.GetProperty("sourceRevision").GetInt64() == committed.Id && payload.RootElement.GetProperty("eventId").GetGuid() == committed.EventId,
                "Source committed payload binds its durable outbox revision and event identity");
            suite.Check(payload.RootElement.GetProperty("startTime").GetDateTime() == SourceSession(7102).StartTime &&
                        payload.RootElement.GetProperty("tankSessions")[0].GetProperty("highPressureAfter").GetString() == "12500",
                "Source enqueuer serializes persisted UTC values and raw measurements without swapping them early");
            var persistedUserCode = await context.Sessions.AsNoTracking()
                .Where(row => row.Id == committed.SourceSessionId).Select(row => row.UserCode).SingleAsync();
            suite.Check(persistedUserCode == PersistedSourceUserCode &&
                        payload.RootElement.GetProperty("userCode").GetString() == persistedUserCode,
                "Source committed snapshot preserves the user code stored in the source database");
        }

        using (var http = new HttpClient())
        {
            var publisher = Publisher(http, host.Address.AbsoluteUri, key);
            var acknowledgement = await publisher.PublishAsync(committed, CancellationToken.None);
            suite.Check(acknowledgement.Delivered && acknowledgement.StatusCode == 200, "Actual source publisher delivers the committed event to the real Core receiver over HTTP");
            var receivedUserCode = await host.DbAsync(context => context.Sessions.AsNoTracking()
                .Where(row => row.LegacyId == committed.SourceSessionId).Select(row => row.UserCode).SingleAsync());
            suite.Check(receivedUserCode == PersistedSourceUserCode,
                "Actual publisher round trip stores the source user code unchanged in Core history");
            var replay = await publisher.PublishAsync(committed, CancellationToken.None);
            suite.Check(replay.Delivered, "Actual source publisher treats durable duplicate acknowledgement as successful delivery");
        }
        await using (var context = Context(connection))
        {
            await new EfCoreHistoryOutboxStore(context).MarkDeliveredAsync(committed.Id, committed.LeaseId, CancellationToken.None);
            suite.Check((await new EfCoreHistoryOutboxStore(context).ClaimAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None)).Count == 0,
                "Acknowledged outbox event is no longer deliverable");
        }

        await StoreCasesAsync(connection, suite);
        await PublisherCasesAsync(committed, key, suite);
        await WorkerCasesAsync(suite);
        await BackfillCases.RunAsync(connection, suite);
    }

    private static SourceEntity.Session SourceSession(long id) => new()
    {
        Id = id, EngineId = 7001, StartTime = new DateTime(2026, 10, 12, 3, 4, 5, DateTimeKind.Unspecified),
        EndTime = new DateTime(2026, 10, 12, 3, 14, 5, DateTimeKind.Unspecified), Mode = "synthetic-source", UserCode = PersistedSourceUserCode,
        TankSessions = [new() { Id = id * 10, TankId = 7001, RFID = "SYNTHETIC-TANK-A", Order = 1,
            HighPressureAfter = "12500", HighPressureBefore = "10000", LoadCellAfter = "40.50", LoadCellBefore = "39.25" }]
    };

    private static async Task StoreCasesAsync(string connection, TestSuite suite)
    {
        await using (var context = Context(connection))
        {
            for (var index = 0; index < 6; index++)
            {
                var id = await NextIdAsync(context);
                context.CoreHistoryOutbox.Add(new() { Id = id, EventId = Guid.NewGuid(), SourceSessionId = 8000 + index, PayloadJson = "{}", CreatedUtc = DateTime.UtcNow, NextAttemptUtc = DateTime.UtcNow });
            }
            await context.SaveChangesAsync();
        }
        var claims = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var context = Context(connection);
            return await new EfCoreHistoryOutboxStore(context).ClaimAsync(2, TimeSpan.FromMinutes(5), CancellationToken.None);
        }));
        var items = claims.SelectMany(batch => batch).ToArray();
        suite.Check(items.Length == 6 && items.Select(row => row.Id).Distinct().Count() == 6 && items.All(row => row.Attempts == 1),
            "Concurrent PostgreSQL workers claim disjoint due rows and increment attempts once");
        await using (var context = Context(connection))
            suite.Check((await new EfCoreHistoryOutboxStore(context).ClaimAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None)).Count == 0,
                "Unexpired leases prevent duplicate delivery by another worker");

        var item = items[0];
        await using (var context = Context(connection))
        {
            var store = new EfCoreHistoryOutboxStore(context);
            await store.MarkDeliveredAsync(item.Id, Guid.NewGuid(), CancellationToken.None);
            await store.ScheduleRetryAsync(item.Id, Guid.NewGuid(), 409, "peer_rejected", DateTime.UtcNow.AddHours(1), CancellationToken.None);
            var unchanged = await context.CoreHistoryOutbox.AsNoTracking().SingleAsync(row => row.Id == item.Id);
            suite.Check(unchanged.DeliveredUtc is null && unchanged.LeaseId == item.LeaseId && unchanged.LastStatusCode is null,
                "A stale worker cannot acknowledge or reschedule another lease");
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"CoreHistoryOutbox\" SET \"LeaseUntilUtc\" = CURRENT_TIMESTAMP AT TIME ZONE 'UTC' - INTERVAL '1 second' WHERE \"Id\" = {item.Id}");
        }
        CoreHistoryOutboxItem reclaimed;
        await using (var context = Context(connection))
            reclaimed = (await new EfCoreHistoryOutboxStore(context).ClaimAsync(1, TimeSpan.FromMinutes(5), CancellationToken.None)).Single();
        suite.Check(reclaimed.Id == item.Id && reclaimed.LeaseId != item.LeaseId && reclaimed.Attempts == 2,
            "Expired lease is recovered with a new fencing token and incremented attempt count");
        await using (var context = Context(connection))
        {
            var store = new EfCoreHistoryOutboxStore(context);
            await store.MarkDeliveredAsync(item.Id, item.LeaseId, CancellationToken.None);
            suite.Check((await context.CoreHistoryOutbox.AsNoTracking().SingleAsync(row => row.Id == item.Id)).DeliveredUtc is null,
                "Old lease acknowledgement cannot defeat a recovered lease");
            await store.ScheduleRetryAsync(reclaimed.Id, reclaimed.LeaseId, 409, "peer_rejected", DateTime.UtcNow.AddMinutes(10), CancellationToken.None);
            var retry = await context.CoreHistoryOutbox.AsNoTracking().SingleAsync(row => row.Id == item.Id);
            suite.Check(retry.DeliveredUtc is null && retry.LeaseId is null && retry.LastStatusCode == 409 && retry.LastErrorCode == "peer_rejected",
                "Permanent-looking receiver conflict remains durably retryable until mapping is repaired");
            suite.Check((await store.ClaimAsync(10, TimeSpan.FromMinutes(5), CancellationToken.None)).Count == 0,
                "Scheduled retry is not claimed before its next-attempt timestamp");
            foreach (var other in items.Skip(1)) await store.MarkDeliveredAsync(other.Id, other.LeaseId, CancellationToken.None);
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"CoreHistoryOutbox\" SET \"NextAttemptUtc\" = CURRENT_TIMESTAMP AT TIME ZONE 'UTC' - INTERVAL '1 second' WHERE \"Id\" = {item.Id}");
        }
        await using (var context = Context(connection))
        {
            var store = new EfCoreHistoryOutboxStore(context);
            var due = (await store.ClaimAsync(1, TimeSpan.FromMinutes(5), CancellationToken.None)).Single();
            suite.Check(due.Id == item.Id && due.EventId == item.EventId && due.Attempts == 3, "Retry preserves the stable delivery event instead of generating a new identity");
            await store.MarkDeliveredAsync(due.Id, due.LeaseId, CancellationToken.None);
        }
    }

    private static async Task<long> NextIdAsync(SourceContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT nextval('public.\"CoreHistoryOutboxIdSequence\"')";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static CoreHistoryPublisher Publisher(HttpClient http, string url, string key) => new(http,
        new FixedOptions<IntegrationPeerOptions>(new() { BaseUrl = url }), new FixedOptions<ApiKeyOptions>(new() { Key = key }), new TestWebEnvironment());

    private static async Task PublisherCasesAsync(CoreHistoryOutboxItem item, string key, TestSuite suite)
    {
        string Ack(Guid eventId, long sourceId, string disposition) => JsonSerializer.Serialize(new { success = true, data = new { eventId, sourceSessionId = sourceId, disposition } });
        foreach (var disposition in new[] { "applied", "duplicate", "stale" })
        {
            var handler = new FakeHttp((_, _) => Task.FromResult(JsonReply(Ack(item.EventId, item.SourceSessionId, disposition))));
            using var http = new HttpClient(handler);
            var result = await Publisher(http, "https://core.invalid/", key).PublishAsync(item, CancellationToken.None);
            suite.Check(result.Delivered && handler.LastMethod == HttpMethod.Post && handler.LastUri?.AbsolutePath == "/api/integration/history/sessions" && handler.KeyCount == 1,
                $"Publisher accepts a bound {disposition} acknowledgement and sends only the dedicated POST endpoint");
        }
        foreach (var response in new[] { Ack(Guid.NewGuid(), item.SourceSessionId, "applied"), Ack(item.EventId, item.SourceSessionId + 1, "applied"), Ack(item.EventId, item.SourceSessionId, "unknown"), "{not-json", "{\"success\":false}", "{}" })
        {
            using var http = new HttpClient(new FakeHttp((_, _) => Task.FromResult(JsonReply(response))));
            var result = await Publisher(http, "https://core.invalid/", key).PublishAsync(item, CancellationToken.None);
            suite.Check(!result.Delivered && result.ErrorCode == "invalid_peer_acknowledgement", "Publisher never marks delivered from malformed or unrelated acknowledgement");
        }
        foreach (var status in new[] { 401, 409, 500, 302 })
        {
            using var http = new HttpClient(new FakeHttp((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("synthetic private error body") })));
            var result = await Publisher(http, "https://core.invalid/", key).PublishAsync(item, CancellationToken.None);
            suite.Check(!result.Delivered && result.StatusCode == status && result.ErrorCode == "peer_rejected", "Publisher retains rejected delivery for retry using only a safe error code");
        }
        foreach (var url in new[] { "", "http://public.invalid/", "https://core.invalid/path", "https://user:password@core.invalid/" })
        {
            var handler = new FakeHttp((_, _) => Task.FromResult(JsonReply("{}"))); using var http = new HttpClient(handler);
            var result = await Publisher(http, url, key).PublishAsync(item, CancellationToken.None);
            suite.Check(!result.Delivered && result.StatusCode == 503 && handler.Calls == 0, "Publisher rejects unsafe or missing peer configuration before sending a history payload");
        }
        var oversizedItem = new CoreHistoryOutboxItem { Id = item.Id, EventId = item.EventId, SourceSessionId = item.SourceSessionId, PayloadJson = new string('x', 1_048_577) };
        var oversizedHandler = new FakeHttp((_, _) => Task.FromResult(JsonReply("{}")));
        using (var http = new HttpClient(oversizedHandler))
        {
            var result = await Publisher(http, "https://core.invalid/", key).PublishAsync(oversizedItem, CancellationToken.None);
            suite.Check(!result.Delivered && result.StatusCode == 413 && oversizedHandler.Calls == 0,
                "Publisher rejects an oversized history request before HTTP using the receiver's one-MiB limit");
        }
        using (var http = new HttpClient(new FakeHttp((_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException()))))
            suite.Check((await Publisher(http, "https://core.invalid/", key).PublishAsync(item, CancellationToken.None)).ErrorCode == "peer_timeout", "Publisher treats transport timeout as retryable failure");
        using (var cancellation = new CancellationTokenSource())
        using (var http = new HttpClient(new FakeHttp((_, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(JsonReply("{}")); })))
        {
            cancellation.Cancel();
            await suite.ThrowsAsync<OperationCanceledException>(() => Publisher(http, "https://core.invalid/", key).PublishAsync(item, cancellation.Token), "Publisher propagates worker shutdown cancellation without an acknowledgement");
        }
    }

    private static async Task WorkerCasesAsync(TestSuite suite)
    {
        var queue = new FakeQueue(); var publisher = new FakePublisher();
        var services = new ServiceCollection().AddSingleton<ICoreHistoryOutboxStore>(queue).AddSingleton<ICoreHistoryPublisher>(publisher);
        using var provider = services.BuildServiceProvider();
        using var worker = new CoreHistoryWorker(provider.GetRequiredService<IServiceScopeFactory>(), new FixedOptions<CoreHistorySyncOptions>(new() { Enabled = true }), NullLogger<CoreHistoryWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        await queue.Done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        suite.Check(queue.Delivered.SequenceEqual([1L]) && queue.Retried.Select(row => row.Id).Order().SequenceEqual([2L, 3L, 4L]),
            "Background worker acknowledges successful delivery and retries rejects, transport errors and thrown publisher failures");
        suite.Check(queue.Retried.All(row => row.NextAttemptUtc > DateTime.UtcNow.AddSeconds(-1)) && queue.Retried.Single(row => row.Id == 4).NextAttemptUtc > DateTime.UtcNow.AddMinutes(59),
            "Worker schedules bounded backoff and retains exhausted-looking attempts for repair");

        var disabledQueue = new FakeQueue();
        using var disabledProvider = new ServiceCollection().AddSingleton<ICoreHistoryOutboxStore>(disabledQueue).AddSingleton<ICoreHistoryPublisher>(new FakePublisher()).BuildServiceProvider();
        using var disabled = new CoreHistoryWorker(disabledProvider.GetRequiredService<IServiceScopeFactory>(), new FixedOptions<CoreHistorySyncOptions>(new() { Enabled = false }), NullLogger<CoreHistoryWorker>.Instance);
        await disabled.StartAsync(CancellationToken.None); await Task.Delay(100); await disabled.StopAsync(CancellationToken.None);
        suite.Check(disabledQueue.Claims == 0, "Disabled background synchronization never claims or sends source histories");
    }

    private static HttpResponseMessage JsonReply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

sealed class FixedOptions<T>(T value) : IOptionsMonitor<T> where T : class
{ public T CurrentValue => value; public T Get(string? name) => value; public IDisposable? OnChange(Action<T, string?> callback) => null; }
sealed class TestWebEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development; public string ApplicationName { get; set; } = "HistorySyncTests";
    public string ContentRootPath { get; set; } = Path.GetTempPath(); public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = Path.GetTempPath(); public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
sealed class FakeHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls; public HttpMethod? LastMethod; public Uri? LastUri; public int KeyCount;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { Calls++; LastMethod = request.Method; LastUri = request.RequestUri; KeyCount = request.Headers.GetValues(IntegrationAuthentication.Header).Count(); return respond(request, cancellationToken); }
}
sealed record RetriedItem(long Id, DateTime NextAttemptUtc);
sealed class FakeQueue : ICoreHistoryOutboxStore
{
    public int Claims; public ConcurrentQueue<long> Delivered { get; } = new(); public ConcurrentQueue<RetriedItem> Retried { get; } = new();
    public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<bool> IsReadyAsync(CancellationToken token) => Task.FromResult(true);
    public Task<IReadOnlyList<CoreHistoryOutboxItem>> ClaimAsync(int count, TimeSpan lease, CancellationToken token)
    { Interlocked.Increment(ref Claims); return Task.FromResult<IReadOnlyList<CoreHistoryOutboxItem>>(Enumerable.Range(1, 4).Select(id => new CoreHistoryOutboxItem { Id = id, EventId = Guid.NewGuid(), LeaseId = Guid.NewGuid(), Attempts = id == 4 ? 30 : 1, PayloadJson = "{}" }).ToArray()); }
    public Task MarkDeliveredAsync(long id, Guid lease, CancellationToken token) { Delivered.Enqueue(id); Finish(); return Task.CompletedTask; }
    public Task ScheduleRetryAsync(long id, Guid lease, int? status, string error, DateTime at, CancellationToken token) { Retried.Enqueue(new(id, at)); Finish(); return Task.CompletedTask; }
    private void Finish() { if (Delivered.Count + Retried.Count == 4) Done.TrySetResult(); }
}
sealed class FakePublisher : ICoreHistoryPublisher
{
    public Task<CoreHistoryDeliveryResult> PublishAsync(CoreHistoryOutboxItem item, CancellationToken token) => item.Id switch
    {
        1 => Task.FromResult(new CoreHistoryDeliveryResult { Delivered = true, StatusCode = 200 }),
        2 => Task.FromResult(new CoreHistoryDeliveryResult { StatusCode = 409, ErrorCode = "peer_rejected" }),
        3 => Task.FromException<CoreHistoryDeliveryResult>(new HttpRequestException("synthetic unreachable peer")),
        _ => Task.FromResult(new CoreHistoryDeliveryResult { StatusCode = 401, ErrorCode = "peer_rejected" })
    };
}
