using CorePlasma.BAL.DTOs.Integration;

namespace CorePlasma.BAL.Interfaces;

public interface ILegacyEventIngestService
{
    Task<LegacyEventIngestResultDto> IngestAsync(LegacyEventIngestDto dto, CancellationToken cancellationToken);
    /// <summary>Internal raw-source bridge. Allocates its own receipt sequence without consuming the native sender's sequence namespace.</summary>
    Task<LegacyEventIngestResultDto> IngestProjectionAsync(LegacyEventIngestDto dto, CancellationToken cancellationToken);
    Task<IReadOnlyList<LegacySourceIdentityDto>> GetIdentitiesAsync(string entity, Guid coreId, CancellationToken cancellationToken);
    Task BindMappingAsync(LegacyMappingBindDto dto, CancellationToken cancellationToken);
}
