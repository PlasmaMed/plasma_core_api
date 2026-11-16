using CorePlasma.BAL.DTOs.Integration;

namespace CorePlasma.BAL.Interfaces;

public interface IHistoryIngestService
{
    Task<HistoryIngestResultDto> IngestAsync(HistorySessionIngestDto dto, CancellationToken cancellationToken);
}
