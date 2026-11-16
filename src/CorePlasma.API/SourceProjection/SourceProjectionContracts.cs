using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CorePlasma.API.SourceSync;

namespace CorePlasma.API.SourceProjection;

public sealed class SourceProjectionOptions
{
    public bool Enabled { get; set; }
    // ConfigurationBinder appends to an initialized array. Require the explicit
    // configured source list so binding never duplicates or broadens it.
    public string[] Sources { get; set; } = [];
    public int PollSeconds { get; set; } = 30;
    public int EventBatchSize { get; set; } = 250;
    /// <summary>Bump after an explicit ownership/canonical resolution to reconsider protected snapshots.</summary>
    public string PolicyVersion { get; set; } = "v1";
    public bool OrdersEnabled { get; set; }
    public bool TransfersEnabled { get; set; }
    public bool TankEngineHistoryEnabled { get; set; }
    public int HistoryBaselineMaxRowsPerPoll { get; set; } = 1000;
    public int HistoryBaselineMaxSeconds { get; set; } = 15;
}

public sealed class SourceProjectionException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed record NativeProjectionState(string Entity, long EntityId, Guid? CoreId, string SnapshotHash,
    string State, string? ErrorCode);

public sealed record NativeProjectionCheckpoint(string SnapshotHash, string State, string? ErrorCode, string Revision);

public static class SourceProjectionIdentity
{
    public static string Namespace(string source) => source switch
    {
        "quanly" => "plasma-bv", "newapp" => "newapp", _ => throw new SourceProjectionException("unknown_source")
    };

    public static string SourceSystem(string source) => source == "quanly" ? "quanly.plasma.vn" : "newapp.plasmamed.vn";

    public static string Hash(JsonElement snapshot) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.GetRawText())));

    public static string RecordHash(string policyVersion, JsonElement snapshot) =>
        Hash(JsonSerializer.SerializeToElement(new { PolicyVersion = policyVersion, Hash = Hash(snapshot) }));

    public static string BaselineHash(Guid generation, SourceProjectionOptions settings, string adapterVersion = SourceSnapshotBuilder.AdapterVersion) =>
        Hash(JsonSerializer.SerializeToElement(new { Generation = generation, settings.PolicyVersion, settings.OrdersEnabled,
            settings.TransfersEnabled, settings.TankEngineHistoryEnabled, AdapterVersion = adapterVersion }));

    public static Guid EventId(string source, Guid installation, Guid generation, string revision, string entity, long id, string hash)
    {
        // Length-prefixed strings avoid ambiguities; the revision is a receipt ID, never a native sequence.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new[] { "source-native-v1", source, installation.ToString("D"),
            generation.ToString("D"), revision, entity, id.ToString(System.Globalization.CultureInfo.InvariantCulture), hash });
        var digest = SHA256.HashData(bytes);
        return new Guid(digest.AsSpan(0, 16));
    }
}

public sealed class SourceProjectionCatalog(IReadOnlyDictionary<string, IReadOnlyList<SourceSyncRawRow>> tables)
{
    private readonly Dictionary<(string Table, string Column), Dictionary<long, SourceSyncRawRow[]>> indexes = [];
    private readonly Dictionary<(string Table, string Column), Dictionary<long, SourceSyncRawRow[]>> groups = [];
    private readonly Dictionary<(string Table, string Column), Dictionary<string, SourceSyncRawRow[]>> textGroups = [];

    public IReadOnlyList<SourceSyncRawRow> Rows(string table) => tables.TryGetValue(table, out var rows)
        ? rows : throw new SourceProjectionException("missing_support_table:" + table);
    public bool ContainsTable(string table) => tables.ContainsKey(table);

    public SourceSyncRawRow? Find(string table, long? id, string column)
    {
        if (id is null || id == 0) return null;
        if (!indexes.TryGetValue((table, column), out var index))
        {
            index = Rows(table).Select(row => (Row: row, Id: ParsedKey(row, column)))
                .Where(item => item.Id is > 0).GroupBy(item => item.Id!.Value)
                .ToDictionary(group => group.Key, group => group.Select(item => item.Row).ToArray());
            indexes[(table, column)] = index;
        }
        var matches = index.GetValueOrDefault(id.Value);
        if (matches is { Length: > 1 }) throw new SourceProjectionException("duplicate_source_identity:" + table);
        return matches?.SingleOrDefault();
    }

    public IReadOnlyList<SourceSyncRawRow> Related(string table, string column, long id)
    {
        if (!groups.TryGetValue((table, column), out var index))
        {
            index = Rows(table).Select(row => (Row: row, Id: ParsedKey(row, column)))
                .Where(item => item.Id is > 0).GroupBy(item => item.Id!.Value)
                .ToDictionary(group => group.Key, group => group.Select(item => item.Row).ToArray());
            groups[(table, column)] = index;
        }
        return index.GetValueOrDefault(id) ?? [];
    }

    public IReadOnlyList<SourceSyncRawRow> RelatedText(string table, string column, string value)
    {
        if (!textGroups.TryGetValue((table, column), out var index))
        {
            index = Rows(table).Where(row => SourceSnapshotBuilder.Text(row.Values, column) is not null)
                .GroupBy(row => SourceSnapshotBuilder.Text(row.Values, column)!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            textGroups[(table, column)] = index;
        }
        return index.GetValueOrDefault(value) ?? [];
    }

    private static long? ParsedKey(SourceSyncRawRow row, string column) => row.Values.TryGetValue(column, out var value) &&
        long.TryParse(value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : null;
}
