using CorePlasma.BAL.DTOs.DataChangeLogs;
using CorePlasma.Common.Responses;

namespace CorePlasma.BAL.Interfaces;

/// <summary>Read-only access to the automatic data change history.</summary>
public interface IDataChangeLogService
{
    Task<PagedResult<DataChangeLogDto>> GetListAsync(DataChangeLogFilterDto filter, CancellationToken cancellationToken);
}
