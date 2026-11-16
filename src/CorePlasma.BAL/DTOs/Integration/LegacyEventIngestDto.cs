using System.Text.Json;
using System.Text.Json.Serialization;

namespace CorePlasma.BAL.DTOs.Integration;

public sealed class LegacyEventIngestDto
{
    [JsonPropertyName("event_id")] public Guid EventId { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), JsonPropertyName("source_system")]
    public string? SourceSystem { get; set; }
    [JsonPropertyName("sequence")] public long Sequence { get; set; }
    [JsonPropertyName("entity")] public string Entity { get; set; } = string.Empty;
    [JsonPropertyName("entity_id")] public long EntityId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), JsonPropertyName("core_id")]
    public Guid? CoreId { get; set; }
    [JsonPropertyName("operation")] public string Operation { get; set; } = string.Empty;
    [JsonRequired, JsonPropertyName("occurred_at"), JsonConverter(typeof(UtcHistoryTimestampConverter))]
    public DateTimeOffset OccurredAt { get; set; }
    [JsonPropertyName("data")] public JsonElement Data { get; set; }
}

public sealed record LegacyEventIngestResultDto(
    [property: JsonPropertyName("event_id")] Guid EventId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("status")] string Status);

public sealed class LegacyMappingBindDto
{
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), JsonPropertyName("source_system")]
    public string? SourceSystem { get; set; }
    [JsonPropertyName("entity")] public string Entity { get; set; } = string.Empty;
    [JsonPropertyName("entity_id")] public long EntityId { get; set; }
    [JsonPropertyName("core_id")] public Guid CoreId { get; set; }
}

public sealed record LegacySourceIdentityDto(string Source, string SourceSystem, long EntityId, bool IsDeleted,
    string? SourceStatusCode = null, string? SourceStatusLabel = null);
