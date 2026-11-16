namespace CorePlasma.BAL.DTOs.Engines;

/// <summary>A fault repeated across completed usages within the current Vietnam calendar day.</summary>
public sealed record EngineErrorWarningDto(
    long Code,
    string Label,
    int OccurrenceCount,
    DateTime LastOccurredAt);
