using AutoMapper;
using CorePlasma.BAL.DTOs.Engines;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Extensions;
using CorePlasma.Common.Responses;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services;

public sealed class EngineService(
    IUnitOfWork unitOfWork,
    IMapper mapper,
    IDocumentNumberService documentNumberService,
    IEngineAccessScope accessScope)
    : CrudServiceBase<Engine, EngineDto, EngineDetailDto, CreateEngineDto, UpdateEngineDto, EngineFilterDto>(
        unitOfWork, mapper, documentNumberService),
      IEngineService
{
    protected override IRepository<Engine> Repository => UnitOfWork.Engines;

    protected override IReadOnlyCollection<string> SortableFields =>
    [
        nameof(Engine.EngineCode), nameof(Engine.EngineName), nameof(Engine.SerialNumber), nameof(Engine.Model),
        nameof(Engine.Manufacturer), nameof(Engine.Department), nameof(Engine.Status), nameof(Engine.CurrentLocation),
        nameof(Engine.NextMaintenanceDate), nameof(Engine.EngineType), nameof(Engine.TankType), nameof(Engine.Store), nameof(Engine.CreatedDate), nameof(Engine.UpdatedDate)
    ];

    protected override AutoCodeOptions<Engine> AutoCode => new(SequenceCodes.Engine, x => x.EngineCode);

    public override async Task<PagedResult<EngineDto>> GetPagedAsync(EngineFilterDto filter, CancellationToken cancellationToken)
    {
        var result = await base.GetPagedAsync(filter, cancellationToken);
        await EnrichRecurringErrorsAsync(result.Items, cancellationToken);
        return result;
    }

    public override async Task<EngineDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await base.GetByIdAsync(id, cancellationToken);
        await EnrichRecurringErrorsAsync([result], cancellationToken);
        return result;
    }

    private async Task EnrichRecurringErrorsAsync(IEnumerable<EngineDto> engines, CancellationToken cancellationToken)
    {
        var items = engines.ToArray();
        var engineIds = items.Select(engine => engine.Id).Distinct().ToArray();
        if (engineIds.Length == 0)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var dayStartUtc = EngineErrorWarnings.GetVietnamDayStartUtc(nowUtc);
        // The returned page/detail already passed ScopeAsync. Restrict this additional query to those IDs
        // and one Vietnam calendar day. Decoding after retrieval handles combined fault masks safely.
        var sessions = await UnitOfWork.Sessions.QueryNoTracking()
            .Where(session => engineIds.Contains(session.EngineId) && session.StartTime >= dayStartUtc &&
                              session.StartTime <= nowUtc && session.EndTime != null &&
                              session.EndTime >= session.StartTime && session.EndTime <= nowUtc)
            .Select(session => new
            {
                session.EngineId,
                Usage = new EngineErrorSession(session.Id, session.LegacyId, session.StartTime, session.EndTime, session.ErrorCode)
            })
            .ToListAsync(cancellationToken);
        var warningsByEngine = sessions.GroupBy(session => session.EngineId)
            .ToDictionary(group => group.Key, group => EngineErrorWarnings.Analyze(group.Select(session => session.Usage), nowUtc));
        foreach (var engine in items)
        {
            engine.RecurringErrorWarnings = warningsByEngine.GetValueOrDefault(engine.Id) ?? [];
        }
    }

    /// <summary>Employees without ENGINE_VIEW_ALL only see (and edit) the machines they are responsible for.</summary>
    protected override async Task<IQueryable<Engine>> ScopeAsync(IQueryable<Engine> query, CancellationToken cancellationToken)
    {
        var employeeId = await accessScope.GetRestrictedEmployeeIdAsync(cancellationToken);
        return employeeId is null ? query : query.Where(x => x.ResponsibleEmployeeId == employeeId);
    }

    protected override async Task<Engine> GetEntityAsync(Guid id, CancellationToken cancellationToken) =>
        await (await ScopeAsync(Repository.Query(), cancellationToken)).FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw new NotFoundException(EntityName, id);

    protected override IQueryable<Engine> ApplyFilter(IQueryable<Engine> query, EngineFilterDto filter)
    {
        var keyword = filter.Keyword.TrimToNull();
        var department = filter.Department.TrimToNull();
        var location = filter.CurrentLocation.TrimToNull();
        var engineType = filter.EngineType.TrimToNull();
        var tankType = filter.TankType.TrimToNull();
        var store = filter.Store.TrimToNull();

        return query
            .WhereContainsAny(UnitOfWork.IsSqlServer, keyword,
                x => x.EngineCode, x => x.EngineName, x => x.SerialNumber, x => x.Model, x => x.Manufacturer)
            .WhereIf(department is not null, x => x.Department == department)
            .WhereContainsAny(UnitOfWork.IsSqlServer, location, x => x.CurrentLocation)
            .WhereIf(filter.ResponsibleEmployeeId.HasValue, x => x.ResponsibleEmployeeId == filter.ResponsibleEmployeeId)
            .WhereIf(filter.Status.HasValue, x => x.Status == filter.Status)
            .WhereIf(engineType is not null, x => x.EngineType == engineType)
            .WhereIf(tankType is not null, x => x.TankType == tankType)
            .WhereIf(store is not null, x => x.Store == store);
    }

    protected override Task BeforeUpdateAsync(Engine entity, UpdateEngineDto dto, CancellationToken cancellationToken) =>
        EnsureUniqueAsync(x => x.EngineCode == dto.EngineCode, entity.Id, "EngineCode already exists.", cancellationToken);
}
