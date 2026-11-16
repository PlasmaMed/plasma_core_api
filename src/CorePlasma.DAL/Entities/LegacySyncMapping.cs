namespace CorePlasma.DAL.Entities;

/// <summary>Explicit source ownership and the last applied immutable source snapshot.</summary>
public sealed class LegacySyncMapping
{
    public string Source { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public long EntityId { get; set; }
    public Guid? CoreId { get; set; }
    public long LastSequence { get; set; }
    public bool PreserveCanonical { get; set; }
    public bool IsDeleted { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; }
}
