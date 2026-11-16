using CorePlasma.BAL.DTOs.DataChangeLogs;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Extensions;
using CorePlasma.Common.Responses;
using CorePlasma.DAL.Extensions;
using CorePlasma.DAL.Interfaces;

namespace CorePlasma.BAL.Services;

public sealed class DataChangeLogService(IUnitOfWork unitOfWork) : IDataChangeLogService
{
    public async Task<PagedResult<DataChangeLogDto>> GetListAsync(
        DataChangeLogFilterDto filter,
        CancellationToken cancellationToken)
    {
        var entityName = filter.EntityName.TrimToNull();
        var entityId = filter.EntityId.TrimToNull();
        var action = filter.Action.TrimToNull();

        var query = unitOfWork.DataChangeLogs.QueryNoTracking()
            .WhereIf(entityName is not null, x => x.EntityName == entityName)
            .WhereIf(entityId is not null, x => x.EntityId == entityId)
            .WhereIf(action is not null, x => x.Action == action)
            .WhereIf(filter.ChangedBy.HasValue, x => x.ChangedBy == filter.ChangedBy)
            .WhereIf(filter.FromDate.HasValue, x => x.ChangedDate >= filter.FromDate)
            .WhereIf(filter.ToDate.HasValue, x => x.ChangedDate <= filter.ToDate)
            .OrderByDescending(x => x.ChangedDate)
            .Select(x => new DataChangeLogDto
            {
                Id = x.Id,
                EntityName = x.EntityName,
                EntityId = x.EntityId,
                Action = x.Action,
                RawChanges = x.Changes,
                ChangedBy = x.ChangedBy,
                ChangedByUsername = x.ChangedByUsername,
                ChangedByUserType = x.ChangedByUserType,
                ChangedDate = x.ChangedDate,
                IpAddress = x.IpAddress,
                CorrelationId = x.CorrelationId
            });

        return await query.ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);
    }
}
