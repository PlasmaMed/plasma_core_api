namespace CorePlasma.Common.Models;

/// <summary>Optional restrictions on ingestion and projection; absent restrictions retain legacy stream selection.</summary>
public sealed class LegacySyncSettings
{
    public const string SectionName = "LegacySync";
    public string[]? AllowedEntities { get; set; }
    public string[]? AllowedSources { get; set; }
    /// <summary>When supplied, only these exact source/entity pairs may ingest, intersected with the older allowlists.</summary>
    public LegacySyncStream[]? AllowedStreams { get; set; }
    /// <summary>Explicit source namespace allowed to project a shared tank, engine or staff profile.</summary>
    public Dictionary<string, string> ProjectionOwners { get; set; } = new(StringComparer.Ordinal);

    public bool Allows(string source, string entity) =>
        (AllowedEntities is null || AllowedEntities.Contains(entity, StringComparer.Ordinal)) &&
        (AllowedSources is null || AllowedSources.Contains(source, StringComparer.Ordinal)) &&
        (AllowedStreams is null || AllowedStreams.Any(x => x.Source == source && x.Entity == entity));

    public bool IsValid() =>
        (AllowedEntities is null || AllowedEntities.All(IsEntity) && AllowedEntities.Distinct(StringComparer.Ordinal).Count() == AllowedEntities.Length) &&
        (AllowedSources is null || AllowedSources.All(IsSource) && AllowedSources.Distinct(StringComparer.Ordinal).Count() == AllowedSources.Length) &&
        (AllowedStreams is null || AllowedStreams.All(x => x is not null && IsEntity(x.Entity) && IsSource(x.Source)) &&
            AllowedStreams.Select(x => (x.Source, x.Entity)).Distinct().Count() == AllowedStreams.Length) &&
        ProjectionOwners is not null && ProjectionOwners.All(x => (x.Key is "tank" or "engine" or "user") && IsSource(x.Value));

    private static bool IsEntity(string value) => value is "customer" or "tank" or "engine" or "user";
    private static bool IsSource(string value) => !string.IsNullOrEmpty(value) && value.Length <= 40 &&
        (value[0] is >= 'a' and <= 'z' or >= '0' and <= '9') &&
        value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
}

public sealed class LegacySyncStream
{
    public string Source { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
}
