namespace CorePlasma.DAL.Entities;

/// <summary>A durable acknowledgement committed atomically with the received NEWAPP snapshot.</summary>
public sealed class HistoryIngestReceipt
{
    public Guid EventId { get; set; }
    public string Source { get; set; } = "NEWAPP";
    public string PayloadHash { get; set; } = string.Empty;
    public long SourceSessionId { get; set; }
    public long SourceRevision { get; set; }
    public Guid SessionId { get; set; }
    public string Disposition { get; set; } = string.Empty;
    public int TankSessionsUpserted { get; set; }
    public DateTime ReceivedAt { get; set; }
}
