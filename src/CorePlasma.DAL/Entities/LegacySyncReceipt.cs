namespace CorePlasma.DAL.Entities;

/// <summary>Committed with the native mutation; acknowledgements can be repeated safely.</summary>
public sealed class LegacySyncReceipt
{
    public Guid EventId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public string Entity { get; set; } = string.Empty;
    public long EntityId { get; set; }
    public Guid? CoreId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = "{}";
    public DateTime OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; }
}
