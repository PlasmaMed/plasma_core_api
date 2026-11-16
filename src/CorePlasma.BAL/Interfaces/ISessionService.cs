using CorePlasma.BAL.DTOs.Sessions;
using CorePlasma.Common.Responses;

namespace CorePlasma.BAL.Interfaces;

/// <summary>Read-only history of machine sessions and the tanks used in them.</summary>
public interface ISessionService
{
    Task<PagedResult<SessionDto>> GetPagedAsync(SessionFilterDto filter, CancellationToken cancellationToken);

    /// <summary>Excel file of the filtered sessions (at most SessionService.ExportMaxRows).</summary>
    Task<byte[]> ExportAsync(SessionFilterDto filter, CancellationToken cancellationToken);

    Task<SessionDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<TankEngineHistoryDto>> GetTankEngineHistoryAsync(TankEngineHistoryFilterDto filter, CancellationToken cancellationToken);
}
