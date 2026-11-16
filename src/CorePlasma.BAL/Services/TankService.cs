using CorePlasma.BAL.DTOs.Sessions;
using AutoMapper;
using CorePlasma.BAL.DTOs.Tanks;
using CorePlasma.BAL.DTOs.Integration;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Extensions;
using CorePlasma.Common.Interfaces;
using CorePlasma.Common.Responses;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using CorePlasma.DAL.Context;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services;

public sealed class TankService(
    IUnitOfWork unitOfWork,
    IMapper mapper,
    IDocumentNumberService documentNumberService,
    IEngineAccessScope engineAccessScope,
    ICurrentUserService currentUser,
    ApplicationDbContext sourceContext)
    : CrudServiceBase<Tank, TankDto, TankDetailDto, CreateTankDto, UpdateTankDto, TankFilterDto>(
        unitOfWork, mapper, documentNumberService),
      ITankService
{
    protected override IRepository<Tank> Repository => UnitOfWork.Tanks;

    protected override IReadOnlyCollection<string> SortableFields =>
    [
        nameof(Tank.TankCode), nameof(Tank.SerialNumber), nameof(Tank.GasType), nameof(Tank.TankType),
        nameof(Tank.Status), nameof(Tank.CurrentLocation), nameof(Tank.InspectionDate),
        nameof(Tank.NextInspectionDate), nameof(Tank.Store), nameof(Tank.CreatedDate), nameof(Tank.UpdatedDate)
    ];

    protected override AutoCodeOptions<Tank> AutoCode => new(SequenceCodes.Tank, x => x.TankCode);

    /// <summary>Machine scope of the caller for the gas-warning filter (ApplyFilter is synchronous), set per request.</summary>
    private Guid? _gasWarningEmployeeScope;

    public override async Task<PagedResult<TankDto>> GetPagedAsync(TankFilterDto filter, CancellationToken cancellationToken)
    {
        if (filter.GasWarning.TrimToNull() is not null)
        {
            _gasWarningEmployeeScope = await engineAccessScope.GetRestrictedEmployeeIdAsync(cancellationToken);
        }

        var result = await base.GetPagedAsync(filter, cancellationToken);
        await EnrichGasReadingsAsync(result.Items, cancellationToken);
        await EnrichSourceIdentitiesAsync(result.Items, cancellationToken);
        return result;
    }

    public override async Task<TankDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await base.GetByIdAsync(id, cancellationToken);
        await EnrichGasReadingsAsync([result], cancellationToken);
        await EnrichSourceIdentitiesAsync([result], cancellationToken);
        return result;
    }

    private async Task EnrichSourceIdentitiesAsync(IEnumerable<TankDto> tanks, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserType != UserType.Employee || currentUser.EmployeeId is null) return;
        var items = tanks.ToArray();
        var ids = items.Select(tank => tank.Id).Distinct().ToArray();
        if (ids.Length == 0) return;
        // Only identities for the already scoped page are read, in one query.
        var aliases = await sourceContext.LegacySyncMappings.AsNoTracking()
            .Where(mapping => mapping.Entity == "tank" && mapping.CoreId.HasValue && ids.Contains(mapping.CoreId.Value))
            .Select(mapping => new { mapping.CoreId, mapping.Source, mapping.SourceSystem, mapping.EntityId, mapping.IsDeleted, mapping.SnapshotJson })
            .ToListAsync(cancellationToken);
        var groups = aliases.GroupBy(alias => alias.CoreId!.Value).ToDictionary(group => group.Key, group =>
            (IReadOnlyList<LegacySourceIdentityDto>)group.OrderBy(alias => alias.SourceSystem).ThenBy(alias => alias.EntityId).Select(alias =>
            {
                var status = SourceTankStatusPresentation.FromSnapshot(alias.SourceSystem, alias.SnapshotJson);
                return new LegacySourceIdentityDto(alias.Source, alias.SourceSystem, alias.EntityId, alias.IsDeleted, status.Code, status.Label);
            }).ToArray());
        foreach (var tank in items) tank.SourceIdentities = groups.GetValueOrDefault(tank.Id) ?? [];
    }

    private async Task EnrichGasReadingsAsync(IEnumerable<TankDto> tanks, CancellationToken cancellationToken)
    {
        // TankService is also used by the customer portal. Machine visibility is an employee permission;
        // a missing employee identity must not become the unrestricted scope used by internal imports.
        if (!currentUser.IsAuthenticated || currentUser.UserType != UserType.Employee || currentUser.EmployeeId is null)
        {
            return;
        }

        var items = tanks.ToArray();
        var ids = items.Select(x => x.Id).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var restrictedEmployeeId = await engineAccessScope.GetRestrictedEmployeeIdAsync(cancellationToken);
        var readings = (await BuildLatestGasReadingQuery(ids).ToListAsync(cancellationToken)).ToDictionary(x => x.TankId);
        foreach (var tank in items)
        {
            if (readings.TryGetValue(tank.Id, out var reading))
            {
                reading.ApplyTo(tank, restrictedEmployeeId);
            }
        }
    }

    /// <summary>
    /// Latest NEWAPP source sequence, even when delivery is delayed. Positive LegacyIds take precedence over records
    /// without a source ID; when there is no source history, insertion time is the fallback.
    /// PostgreSQL uses the (TankId, COALESCE(LegacyId, 0), Id) expression index for one probe per tank.
    /// Device clocks are sometimes wrong (years 1970, 2032), so StartTime is not used to pick it.
    /// </summary>
    private IQueryable<TankGasReading> BuildLatestGasReadingQuery(Guid[] tankIds)
    {
        var sessions = UnitOfWork.Sessions.QueryNoTracking();
        var tankSessions = UnitOfWork.TankSessions.QueryNoTracking();

        if (UnitOfWork.IsSqlServer)
        {
            // SQL Server sorts uniqueidentifier differently from UUID v7 chronological order.
            // Imported source IDs are positive; DESC puts null behind them without COALESCE,
            // allowing the recency indexes to satisfy the ordering.
            return from tank in UnitOfWork.Tanks.QueryNoTracking().Where(x => tankIds.Contains(x.Id))
                   from usage in tankSessions.Where(x => x.TankId == tank.Id)
                       .OrderByDescending(x => tank.Id).ThenByDescending(x => x.LegacyId)
                       .ThenByDescending(x => x.CreatedDate).ThenByDescending(x => x.Id).Take(1)
                   select new TankGasReading
                   {
                       TankId = tank.Id,
                       EngineId = usage.Session.EngineId,
                       EngineCode = usage.Session.Engine.EngineCode,
                       ResponsibleEmployeeId = usage.Session.Engine.ResponsibleEmployeeId,
                       StartTime = usage.Session.StartTime,
                       EndTime = usage.Session.EndTime,
                       PressureBefore = usage.PressureBefore,
                       PressureAfter = usage.PressureAfter,
                       IsLatestEngineSession = sessions.Where(x => x.EngineId == usage.Session.EngineId)
                           .OrderByDescending(x => x.LegacyId).ThenByDescending(x => x.CreatedDate)
                           .ThenByDescending(x => x.Id).Select(x => x.Id).FirstOrDefault() == usage.SessionId
                   };
        }

        return from tank in UnitOfWork.Tanks.QueryNoTracking().Where(x => tankIds.Contains(x.Id))
               // The outer tank.Id in the ordering keeps a correlated LATERAL ... LIMIT 1 per tank.
               // Without it EF rewrites this to ROW_NUMBER() over the whole TankSessions history (~20 s).
               from usage in tankSessions.Where(x => x.TankId == tank.Id)
                   .OrderByDescending(x => tank.Id).ThenByDescending(x => x.LegacyId ?? 0L).ThenByDescending(x => x.Id).Take(1)
               select new TankGasReading
               {
                   TankId = tank.Id,
                   EngineId = usage.Session.EngineId,
                   EngineCode = usage.Session.Engine.EngineCode,
                   ResponsibleEmployeeId = usage.Session.Engine.ResponsibleEmployeeId,
                   StartTime = usage.Session.StartTime,
                   EndTime = usage.Session.EndTime,
                   PressureBefore = usage.PressureBefore,
                   PressureAfter = usage.PressureAfter,
                   IsLatestEngineSession = sessions.Where(x => x.EngineId == usage.Session.EngineId)
                       .OrderByDescending(x => x.LegacyId ?? 0L).ThenByDescending(x => x.Id)
                       .Select(x => x.Id).FirstOrDefault() == usage.SessionId
               };
    }

    /// <summary>Raw pressure below 10 % (10 × 125): the "Low" threshold of <see cref="TankGasReading"/>.</summary>
    private const decimal LowGasPressure = 10m * TankSessionDto.PressureUnitsPerPercent;

    /// <summary>
    /// Same rule as <see cref="TankGasReading.ApplyTo"/>, evaluated in SQL so the whole list can be filtered:
    /// tank at the customer / in use, its latest recorded usage is in the machine's latest session, and the reading
    /// after use (before use while the session runs) is 0 (Empty) or above 0 and below 10 % (Low).
    /// Starts from the ~2 000 machines' latest sessions instead of probing the history of every tank (0.5 s vs 4 s).
    /// </summary>
    private IQueryable<Tank> ApplyGasWarningFilter(IQueryable<Tank> query, string? warning)
    {
        if (warning is null)
        {
            return query;
        }

        var low = !string.Equals(warning, "Empty", StringComparison.OrdinalIgnoreCase);
        var empty = !string.Equals(warning, "Low", StringComparison.OrdinalIgnoreCase);
        var employeeId = _gasWarningEmployeeScope;
        var tankSessions = UnitOfWork.TankSessions.QueryNoTracking();
        var sessions = UnitOfWork.Sessions.QueryNoTracking();

        // The outer e.Id in the ordering keeps a LATERAL ... LIMIT 1 per machine (see BuildLatestGasReadingQuery).
        IQueryable<Guid> warnedTankIds;
        if (UnitOfWork.IsSqlServer)
        {
            warnedTankIds =
                from e in UnitOfWork.Engines.QueryNoTracking()
                where employeeId == null || e.ResponsibleEmployeeId == employeeId
                from s in sessions.Where(x => x.EngineId == e.Id).OrderByDescending(x => e.Id)
                    .ThenByDescending(x => x.LegacyId).ThenByDescending(x => x.CreatedDate)
                    .ThenByDescending(x => x.Id).Take(1)
                from ts in tankSessions.Where(x => x.SessionId == s.Id && x.TankId != null)
                let pressure = s.EndTime != null ? ts.PressureAfter : ts.PressureBefore
                where ts.Id == tankSessions.Where(x => x.TankId != null && x.TankId.Value == ts.TankId!.Value)
                          .OrderByDescending(x => x.LegacyId).ThenByDescending(x => x.CreatedDate)
                          .ThenByDescending(x => x.Id).Select(x => x.Id).FirstOrDefault()
                      && ((empty && pressure == 0m) || (low && pressure > 0m && pressure < LowGasPressure))
                select ts.TankId!.Value;
        }
        else
        {
            warnedTankIds =
                from e in UnitOfWork.Engines.QueryNoTracking()
                where employeeId == null || e.ResponsibleEmployeeId == employeeId
                from s in sessions.Where(x => x.EngineId == e.Id).OrderByDescending(x => e.Id)
                    .ThenByDescending(x => x.LegacyId ?? 0L).ThenByDescending(x => x.Id).Take(1)
                from ts in tankSessions.Where(x => x.SessionId == s.Id && x.TankId != null)
                let pressure = s.EndTime != null ? ts.PressureAfter : ts.PressureBefore
                // .Value on both sides: both are non-null here, and it stops EF adding "OR (both IS NULL)",
                // which would prevent the (TankId, Id) index from being used (30 s instead of 0.5 s).
                where ts.Id == tankSessions.Where(x => x.TankId != null && x.TankId.Value == ts.TankId!.Value)
                          .OrderByDescending(x => x.LegacyId ?? 0L).ThenByDescending(x => x.Id).Select(x => x.Id).FirstOrDefault()
                      && ((empty && pressure == 0m) || (low && pressure > 0m && pressure < LowGasPressure))
                select ts.TankId!.Value;
        }

        return query.Where(x => (x.Status == TankStatus.AtClient || x.Status == TankStatus.InUse) && warnedTankIds.Contains(x.Id));
    }

    protected override IQueryable<Tank> ApplyFilter(IQueryable<Tank> query, TankFilterDto filter)
    {
        var keyword = filter.Keyword.TrimToNull();
        var tankCode = filter.TankCode.TrimToNull();
        var serialNumber = filter.SerialNumber.TrimToNull();
        var gasType = filter.GasType.TrimToNull();
        var location = filter.CurrentLocation.TrimToNull();
        var tankType = filter.TankType.TrimToNull();
        var store = filter.Store.TrimToNull();
        var keywordCodeAliases = CodeAliases.For(keyword, 50);
        var keywordSerialAliases = CodeAliases.For(keyword, 100);
        var tankCodeAliases = CodeAliases.For(tankCode, 50);
        var serialNumberAliases = CodeAliases.For(serialNumber, 100);

        return ApplyGasWarningFilter(query, filter.GasWarning.TrimToNull())
            .WhereContainsAnyOr(UnitOfWork.IsSqlServer, keyword, x =>
                    keywordCodeAliases.Contains(x.TankCode.Trim().ToUpper()) ||
                    (x.SerialNumber != null && keywordSerialAliases.Contains(x.SerialNumber.Trim().ToUpper())),
                x => x.TankCode, x => x.SerialNumber, x => x.CurrentLocation,
                x => x.Customer != null ? x.Customer.CustomerName : null)
            .WhereContainsAnyOr(UnitOfWork.IsSqlServer, tankCode,
                x => tankCodeAliases.Contains(x.TankCode.Trim().ToUpper()), x => x.TankCode)
            .WhereContainsAnyOr(UnitOfWork.IsSqlServer, serialNumber,
                x => x.SerialNumber != null && serialNumberAliases.Contains(x.SerialNumber.Trim().ToUpper()),
                x => x.SerialNumber)
            .WhereIf(gasType is not null, x => x.GasType == gasType)
            .WhereContainsAny(UnitOfWork.IsSqlServer, location, x => x.CurrentLocation)
            .WhereIf(filter.CustomerId.HasValue, x => x.CustomerId == filter.CustomerId)
            .WhereIf(filter.AssignedEmployeeId.HasValue, x => x.AssignedEmployeeId == filter.AssignedEmployeeId)
            .WhereIf(filter.Status.HasValue, x => x.Status == filter.Status)
            .WhereIf(tankType is not null, x => x.TankType == tankType)
            .WhereIf(store is not null, x => x.Store == store);
    }

    protected override async Task BeforeUpdateAsync(Tank entity, UpdateTankDto dto, CancellationToken cancellationToken)
    {
        await EnsureUniqueAsync(x => x.TankCode == dto.TankCode, entity.Id, "TankCode already exists.", cancellationToken);

        if (!string.IsNullOrWhiteSpace(dto.SerialNumber))
        {
            await EnsureUniqueAsync(x => x.SerialNumber == dto.SerialNumber, entity.Id, "SerialNumber already exists.", cancellationToken);
        }
    }

    protected override Task BeforeDeleteAsync(Tank entity, CancellationToken cancellationToken)
    {
        if (entity.Status == TankStatus.InUse)
        {
            throw new BusinessException("Cannot delete a tank that is in use.");
        }

        return Task.CompletedTask;
    }
}
