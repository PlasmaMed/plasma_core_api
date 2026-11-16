using System.Text.Json;
using System.Text.Json.Serialization;

namespace CorePlasma.API.SourceSync;

public sealed class SourceSyncOptions
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = "";
    public Dictionary<string, SourceSyncSourceOptions> Sources { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SourceSyncSourceOptions
{
    public Guid InstallationId { get; set; }
    public string Key { get; set; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceSyncTable(string Name, string[] Columns, string[] PrimaryKey, string CatalogSignature);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceSyncBegin(Guid InstallationId, Guid Generation, string Snapshot, SourceSyncTable[] Tables);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceSyncRows(Guid InstallationId, Guid Generation, string Table, Guid ChunkId, JsonElement[] Rows);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceSyncTableCount(string Name, string RowCount);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceSyncComplete(Guid InstallationId, Guid Generation, SourceSyncTableCount[] Tables);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceSyncEvent(string Id, string TransactionId, string Table, string Operation,
    [property: JsonRequired] JsonElement OldRow, [property: JsonRequired] JsonElement NewRow, string OccurredAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceSyncEvents(Guid InstallationId, Guid Generation, Guid BatchId, SourceSyncEvent[] Events);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceSyncEventPart(Guid InstallationId, Guid Generation, Guid BatchId, int PartIndex, SourceSyncEvent[] Events);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceSyncEventCommit(Guid InstallationId, Guid Generation, Guid BatchId, int PartCount, string EventCount);

public sealed record SourceSyncState(string Source, Guid InstallationId, Guid Generation, string Status,
    int TableCount, long PendingProjectionCount, string ProjectionState, string? ErrorCode = null, long PendingBatchCount = 0);
public sealed record SourceSyncCompletionAck(string Source, Guid InstallationId, Guid Generation, string Status);
public sealed record SourceSyncRawRow(string Table, IReadOnlyDictionary<string, string?> Values, long Occurrences = 1);
public sealed record SourceSyncRawPage(IReadOnlyList<SourceSyncRawRow> Rows, string? NextCursor);
public sealed record SourceSyncPendingEvent(string Source, Guid InstallationId, Guid Generation,
    string EventId, SourceSyncEvent Event, string ProjectionState);

/// <summary>Private raw data access for explicit, verified native adapters; never exposed by the HTTP status route.</summary>
public interface ISourceSyncRawReader
{
    Task<SourceSyncState?> GetStateAsync(string source, CancellationToken cancellationToken);
    Task<IReadOnlyList<SourceSyncTable>> GetTablesAsync(string source, CancellationToken cancellationToken);
    IAsyncEnumerable<SourceSyncRawRow> ReadRowsAsync(string source, string table, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, IReadOnlyList<SourceSyncRawRow>>> ReadProjectionSnapshotAsync(
        string source, string[] tables, CancellationToken cancellationToken);
    Task<SourceSyncRawRow?> ReadRowAsync(string source, string table,
        IReadOnlyDictionary<string, string?> primaryKey, CancellationToken cancellationToken);
    Task<SourceSyncRawPage> ReadPageAsync(string source, string table, string? afterKey, int limit,
        IReadOnlyDictionary<string, string?>? exactFilters, string? search, string[]? searchColumns,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SourceSyncPendingEvent>> ReadPendingAsync(string source, int limit, CancellationToken cancellationToken);
    Task SetProjectionStateAsync(string source, Guid installationId, string eventId,
        string state, string? errorCode, CancellationToken cancellationToken);
    Task SetBaselineProjectionStateAsync(string source, Guid generation,
        string state, string? errorCode, CancellationToken cancellationToken);
}
