namespace CorePlasma.DAL.Entities;

/// <summary>Source-owned business domains without a dedicated Core workflow model.</summary>
public sealed class CoreBusinessRecord : BaseEntity
{
    public string Source { get; set; } = "";
    public string SourceTable { get; set; } = "";
    public string Domain { get; set; } = "";
    public Guid InstallationId { get; set; }
    public Guid Generation { get; set; }
    public string SourceKeyHash { get; set; } = "";
    public string ExternalKeyJson { get; set; } = "{}";
    public string Name { get; set; } = "";
    public string? SourceStatus { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public Guid? CanonicalEntityId { get; set; }
    public string? LastRawEventId { get; set; }
    public string SnapshotHash { get; set; } = "";
    public Guid LastSeenRunId { get; set; }
}
