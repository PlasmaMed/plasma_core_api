using System.Text.Json.Serialization;
using System.Text.Json;

namespace CorePlasma.BAL.DTOs.Integration;

/// <summary>A committed NEWAPP session snapshot. Source IDs belong only to the canonical NEWAPP database.</summary>
public sealed class HistorySessionIngestDto
{
    public string Source { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public long SourceRevision { get; set; }
    public long SourceSessionId { get; set; }
    /// <summary>Optional stable NEWAPP Engine.Id, usable only after its Core identity has been verified.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SourceEngineId { get; set; }
    public string EngineSerial { get; set; } = string.Empty;
    [JsonRequired]
    [JsonConverter(typeof(UtcHistoryTimestampConverter))]
    public DateTimeOffset StartTime { get; set; }
    [JsonConverter(typeof(UtcHistoryTimestampConverter))]
    public DateTimeOffset? EndTime { get; set; }
    public string? Mode { get; set; }
    public string? AverageSpeed { get; set; }
    public string? PlasmaOnTime { get; set; }
    public string? EnvironmentTemperatureBefore { get; set; }
    public string? EnvironmentTemperatureAfter { get; set; }
    public string? HandleTemperatureBefore { get; set; }
    public string? HandleTemperatureAfter { get; set; }
    public string? ErrorCode { get; set; }
    public string? UserCode { get; set; }
    public List<HistoryTankSessionIngestDto> TankSessions { get; set; } = [];
}

public sealed class HistoryTankSessionIngestDto
{
    public long SourceTankSessionId { get; set; }
    /// <summary>Optional stable NEWAPP Tank.Id; it must already have a verified Core identity.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SourceTankId { get; set; }
    public string? TankRfid { get; set; }
    public string? TankSerial { get; set; }
    public string? TankLegacyId { get; set; }
    public int? Order { get; set; }
    public string? HighPressureBefore { get; set; }
    public string? HighPressureAfter { get; set; }
    public string? LoadCellBefore { get; set; }
    public string? LoadCellAfter { get; set; }
}

public sealed class HistoryIngestResultDto
{
    public Guid EventId { get; set; }
    public long SourceSessionId { get; set; }
    public string Disposition { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
    public int TankSessionsUpserted { get; set; }
}

/// <summary>Never infer an absent timezone from the receiver host's local timezone.</summary>
public sealed class UtcHistoryTimestampConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (text is null || !(text.EndsWith('Z') || text.EndsWith("+00:00", StringComparison.Ordinal) || text.EndsWith("-00:00", StringComparison.Ordinal)) ||
            !reader.TryGetDateTimeOffset(out var value) || value.Offset != TimeSpan.Zero)
            throw new JsonException("History timestamps must contain an explicit UTC offset.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToUniversalTime());
}
