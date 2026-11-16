using System.Linq.Expressions;
using CorePlasma.BAL.DTOs.Sessions;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Extensions;
using CorePlasma.Common.Responses;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Extensions;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services;

public sealed class SessionService(IUnitOfWork unitOfWork, IEngineAccessScope accessScope) : ISessionService
{
    // TankSession.SessionId → Session.Id → Session.EngineId → Engine.Id
    private static readonly Expression<Func<Session, SessionDto>> ToDto = x => new SessionDto
    {
        Id = x.Id,
        EngineId = x.EngineId,
        EngineCode = x.Engine.EngineCode,
        EngineSerial = x.Engine.SerialNumber,
        EngineName = x.Engine.EngineName,
        StartTime = x.StartTime,
        EndTime = x.EndTime,
        Mode = x.Mode,
        AverageSpeed = x.AverageSpeed,
        PlasmaOnTime = x.PlasmaOnTime,
        EnvironmentTemperatureBefore = x.EnvironmentTemperatureBefore,
        EnvironmentTemperatureAfter = x.EnvironmentTemperatureAfter,
        HandleTemperatureBefore = x.HandleTemperatureBefore,
        HandleTemperatureAfter = x.HandleTemperatureAfter,
        ErrorCode = x.ErrorCode,
        UserCode = x.UserCode,
        LegacyId = x.LegacyId,
        Tanks = x.TankSessions
            .OrderBy(t => t.Order).ThenBy(t => t.LegacyId).ThenBy(t => t.CreatedDate)
            .Select(t => new TankSessionDto
            {
                Id = t.Id,
                TankRfid = t.TankRfid,
                TankId = t.TankId,
                TankCode = t.Tank != null ? t.Tank.TankCode : null,
                Order = t.Order,
                PressureBefore = t.PressureBefore,
                PressureAfter = t.PressureAfter,
                WeightBefore = t.WeightBefore,
                WeightAfter = t.WeightAfter
            })
            .ToList()
    };

    /// <summary>Upper bound of one Excel export.</summary>
    public const int ExportMaxRows = 100_000;

    /// <summary>Times are stored in UTC and shown in Vietnam time.</summary>
    private static readonly TimeSpan DisplayOffset = TimeSpan.FromHours(7);

    public async Task<PagedResult<SessionDto>> GetPagedAsync(SessionFilterDto filter, CancellationToken cancellationToken) =>
        await (await FilterAsync(filter, cancellationToken)).Select(ToDto).ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);

    public async Task<byte[]> ExportAsync(SessionFilterDto filter, CancellationToken cancellationToken)
    {
        var query = await FilterAsync(filter, cancellationToken);
        var total = await query.CountAsync(cancellationToken);
        if (total > ExportMaxRows)
        {
            throw new BusinessException($"Có {total:N0} phiên, vượt giới hạn {ExportMaxRows:N0} phiên mỗi lần tải. Hãy thu hẹp khoảng thời gian.");
        }

        var sessions = await query.Select(ToDto).ToListAsync(cancellationToken);
        return SessionExcelWriter.Write(sessions, DisplayOffset);
    }

    private async Task<IQueryable<Session>> FilterAsync(SessionFilterDto filter, CancellationToken cancellationToken)
    {
        var responsibleEmployeeId = await accessScope.GetRestrictedEmployeeIdAsync(cancellationToken);
        var serial = filter.Serial.TrimToNull();
        var tankRfid = filter.TankRfid.TrimToNull()?.ToUpperInvariant();

        return unitOfWork.Sessions.QueryNoTracking()
            .WhereIf(responsibleEmployeeId.HasValue, x => x.Engine.ResponsibleEmployeeId == responsibleEmployeeId)
            .WhereIf(filter.EngineId.HasValue, x => x.EngineId == filter.EngineId)
            .WhereIf(filter.TankId.HasValue, x => x.TankSessions.Any(t => t.TankId == filter.TankId))
            .WhereExactAny(unitOfWork.IsSqlServer, serial, x => x.Engine.SerialNumber, x => x.Engine.EngineCode)
            // upper("TankRfid") = @p uses the expression index on TankSessions.
            .WhereIf(tankRfid is not null, x => x.TankSessions.Any(t => t.TankRfid != null && t.TankRfid.ToUpper() == tankRfid))
            .WhereIf(filter.FromDate.HasValue, x => x.StartTime >= filter.FromDate)
            .WhereIf(filter.ToDate.HasValue, x => x.StartTime <= filter.ToDate)
            .OrderByDescending(x => x.StartTime);
    }

    public async Task<PagedResult<TankEngineHistoryDto>> GetTankEngineHistoryAsync(
        TankEngineHistoryFilterDto filter,
        CancellationToken cancellationToken)
    {
        var serial = filter.Serial.TrimToNull();
        var tankRfid = filter.TankRfid.TrimToNull()?.ToUpperInvariant();

        var responsibleEmployeeId = await accessScope.GetRestrictedEmployeeIdAsync(cancellationToken);
        var query = unitOfWork.TankEngineHistories.QueryNoTracking()
            .WhereIf(responsibleEmployeeId.HasValue, x => x.Engine.ResponsibleEmployeeId == responsibleEmployeeId)
            .WhereIf(filter.EngineId.HasValue, x => x.EngineId == filter.EngineId)
            .WhereExactAny(unitOfWork.IsSqlServer, serial, x => x.Engine.SerialNumber, x => x.Engine.EngineCode)
            .WhereIf(tankRfid is not null, x => x.TankRfid != null && x.TankRfid.ToUpper() == tankRfid)
            .WhereIf(filter.EventType.HasValue, x => x.EventType == filter.EventType)
            .WhereIf(filter.FromDate.HasValue, x => x.EventTime >= filter.FromDate)
            .WhereIf(filter.ToDate.HasValue, x => x.EventTime <= filter.ToDate)
            .OrderByDescending(x => x.EventTime)
            .Select(x => new TankEngineHistoryDto
            {
                Id = x.Id,
                EngineId = x.EngineId,
                EngineCode = x.Engine.EngineCode,
                EngineSerial = x.Engine.SerialNumber,
                TankRfid = x.TankRfid,
                TankId = x.TankId,
                TankCode = x.Tank != null ? x.Tank.TankCode : null,
                EventTime = x.EventTime,
                EventType = x.EventType
            });

        return await query.ToPagedResultAsync(filter.Page, filter.PageSize, cancellationToken);
    }

    public async Task<SessionDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var responsibleEmployeeId = await accessScope.GetRestrictedEmployeeIdAsync(cancellationToken);
        return await unitOfWork.Sessions.QueryNoTracking()
                   .WhereIf(responsibleEmployeeId.HasValue, x => x.Engine.ResponsibleEmployeeId == responsibleEmployeeId)
                   .Where(x => x.Id == id).Select(ToDto).FirstOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException("Session not found.");
    }
}
