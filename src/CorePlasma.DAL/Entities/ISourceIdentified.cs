namespace CorePlasma.DAL.Entities;

/// <summary>Searchable representative IDs; LegacySyncMappings retains all authoritative aliases.</summary>
public interface ISourceIdentified
{
    long? QuanLyId { get; set; }
    long? NewAppId { get; set; }
}
