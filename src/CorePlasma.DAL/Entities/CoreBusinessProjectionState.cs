namespace CorePlasma.DAL.Entities;

/// <summary>Completed baseline fingerprint; events advance records independently after this checkpoint.</summary>
public sealed class CoreBusinessProjectionState
{
    public string Source { get; set; } = "";
    public Guid InstallationId { get; set; }
    public Guid Generation { get; set; }
    public string CatalogHash { get; set; } = "";
    public DateTime CompletedAt { get; set; }
}
