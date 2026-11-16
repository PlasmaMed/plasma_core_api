using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CorePlasma.BAL.DTOs.Integration;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Common;
using CorePlasma.BAL.Validators;
using CorePlasma.Common.Exceptions;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services;

public sealed class HistoryIngestService(ApplicationDbContext context, IValidator<HistorySessionIngestDto> validator) : IHistoryIngestService
{
    public async Task<HistoryIngestResultDto> IngestAsync(HistorySessionIngestDto dto, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(dto, cancellationToken);
        if (dto.SourceEngineId is <= 0)
            throw new BusinessException("SourceEngineId must be a positive NEWAPP engine ID when supplied.");
        var payloadHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(dto)));
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Every writer takes locks in the same order, including child IDs and receipts shared across sessions.
            // Stable SHA256-derived bigint keys avoid process-dependent string hashing and namespace collisions.
            var lockKeys = dto.TankSessions.Select(t => LockKey($"NEWAPP:tank-session:{t.SourceTankSessionId}"))
                .Append(LockKey($"NEWAPP:session:{dto.SourceSessionId}"))
                .Append(LockKey($"NEWAPP:event:{dto.EventId:D}"))
                .Append(LockKey($"NEWAPP:revision:{dto.SourceRevision}"))
                .Distinct().Order();
            foreach (var key in lockKeys)
                await TransactionAdvisoryLock.AcquireAsync(context.Database, key, cancellationToken);

            var receipt = await context.HistoryIngestReceipts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.EventId == dto.EventId, cancellationToken);
            if (receipt is not null)
            {
                if (!string.Equals(receipt.PayloadHash, payloadHash, StringComparison.Ordinal))
                    throw new ConflictException("EventId has already been used for a different history payload.");
                await transaction.CommitAsync(cancellationToken);
                return Result(dto, receipt.SessionId, "duplicate", 0);
            }

            if (await context.HistoryIngestReceipts.AnyAsync(x => x.Source == "NEWAPP" &&
                    x.SourceRevision == dto.SourceRevision, cancellationToken))
                throw new ConflictException("SourceRevision has already been used for a different event.");

            var engineId = await ResolveEngineAsync(dto.EngineSerial, dto.SourceEngineId, cancellationToken);
            var session = await context.Sessions.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.LegacyId == dto.SourceSessionId, cancellationToken);
            if (session is { IsDeleted: true })
                throw new ConflictException("The source session refers to a deleted Core history record.");
            if (session is not null && session.EngineId != engineId)
                throw new ConflictException("The source session is already linked to a different Core engine.");

            var childIds = dto.TankSessions.Select(x => x.SourceTankSessionId).ToArray();
            var children = await context.TankSessions.IgnoreQueryFilters()
                .Where(x => x.LegacyId.HasValue && childIds.Contains(x.LegacyId.Value)).ToListAsync(cancellationToken);
            if (children.Any(x => x.IsDeleted))
                throw new ConflictException("A source tank session refers to a deleted Core history record.");
            if (children.Any(x => session is null || x.SessionId != session.Id))
                throw new ConflictException("A source tank session is already linked to a different session.");

            var disposition = session is not null && dto.SourceRevision <= session.SourceRevision ? "stale" : "applied";
            var upserted = 0;
            if (disposition == "applied")
            {
                if (session is null)
                {
                    session = new Session { EngineId = engineId, LegacyId = dto.SourceSessionId };
                    context.Sessions.Add(session);
                }
                ApplySession(dto, session);
                var existing = children.ToDictionary(x => x.LegacyId!.Value);
                foreach (var incoming in dto.TankSessions)
                {
                    if (!existing.TryGetValue(incoming.SourceTankSessionId, out var child))
                    {
                        child = new TankSession { SessionId = session.Id, LegacyId = incoming.SourceTankSessionId };
                        context.TankSessions.Add(child);
                    }
                    var tankId = await ResolveTankAsync(incoming, cancellationToken);
                    if (child.TankId is not null && tankId is not null && child.TankId != tankId)
                        throw new ConflictException("The source tank session is already linked to a different Core tank.");
                    child.TankRfid = incoming.TankRfid;
                    // Missing legacy hints must not erase a previously resolved relationship.
                    child.TankId = tankId ?? child.TankId;
                    child.Order = incoming.Order;
                    // NEWAPP's Before/After names are reversed. Match the established SQL import exactly once.
                    child.PressureBefore = Measurement(incoming.HighPressureAfter);
                    child.PressureAfter = Measurement(incoming.HighPressureBefore);
                    child.WeightBefore = Measurement(incoming.LoadCellAfter);
                    child.WeightAfter = Measurement(incoming.LoadCellBefore);
                    upserted++;
                }
            }

            context.HistoryIngestReceipts.Add(new HistoryIngestReceipt
            {
                EventId = dto.EventId, Source = "NEWAPP", PayloadHash = payloadHash,
                SourceSessionId = dto.SourceSessionId, SourceRevision = dto.SourceRevision,
                SessionId = session!.Id, Disposition = disposition, TankSessionsUpserted = upserted,
                ReceivedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result(dto, session.Id, disposition, upserted);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<Guid> ResolveEngineAsync(string serial, long? sourceEngineId, CancellationToken cancellationToken)
    {
        if (sourceEngineId is { } sourceId)
        {
            // Source IDs never establish ownership. They may resolve only a previously
            // verified identity, allowing immutable older history packets after a serial rename.
            var mapping = await context.LegacySyncMappings.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Source == "newapp" && x.Entity == "engine" && x.EntityId == sourceId, cancellationToken);
            if (mapping is not null && (mapping.SourceSystem != "newapp.plasmamed.vn" || mapping.IsDeleted || mapping.CoreId is null))
                throw new ConflictException("The NEWAPP engine identity is deleted or has no verified Core target.");
            var columnMatches = await context.Engines.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.NewAppId == sourceId)
                .Select(x => new { x.Id, x.IsDeleted }).Take(2).ToListAsync(cancellationToken);
            if (columnMatches.Count > 1 || (mapping is not null &&
                columnMatches.Any(x => x.Id != mapping.CoreId)))
                throw new ConflictException("The NEWAPP engine identity conflicts with its verified Core mapping.");
            var coreId = mapping?.CoreId ?? columnMatches.Select(x => (Guid?)x.Id).SingleOrDefault();
            if (coreId is null)
                throw new ConflictException("SourceEngineId has no verified Core engine mapping. Synchronize the engine and retry.");
            var engine = await context.Engines.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Id == coreId).Select(x => new { x.Id, x.IsDeleted, x.NewAppId })
                .SingleOrDefaultAsync(cancellationToken);
            if (engine is null || engine.IsDeleted ||
                (engine.NewAppId is not null && engine.NewAppId != sourceId))
                throw new ConflictException("The verified NEWAPP engine is missing, deleted or bound to another source ID.");
            return engine.Id;
        }

        // Legacy payloads omit SourceEngineId and retain exactly their original serial lookup.
        var normalized = serial.Trim().ToUpperInvariant();
        var candidates = await context.Engines.AsNoTracking()
            .Where(x => x.SerialNumber != null && x.SerialNumber.Trim().ToUpper() == normalized)
            .Select(x => x.Id).Take(2).ToListAsync(cancellationToken);
        if (candidates.Count == 0)
            candidates = await context.Engines.AsNoTracking().Where(x => x.EngineCode.Trim().ToUpper() == normalized)
                .Select(x => x.Id).Take(2).ToListAsync(cancellationToken);
        return candidates.Count == 1 ? candidates[0]
            : throw new ConflictException(candidates.Count == 0
                ? "EngineSerial does not identify an existing Core engine. Configure the mapping and retry."
                : "EngineSerial is ambiguous in Core. Resolve the mapping and retry.");
    }

    private async Task<Guid?> ResolveTankAsync(HistoryTankSessionIngestDto dto, CancellationToken cancellationToken)
    {
        if (dto.SourceTankId is { } sourceId)
        {
            var mapping = await context.LegacySyncMappings.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Source == "newapp" && x.Entity == "tank" && x.EntityId == sourceId, cancellationToken);
            if (mapping is not null && (mapping.SourceSystem != "newapp.plasmamed.vn" || mapping.IsDeleted || mapping.CoreId is null))
                throw new ConflictException("The NEWAPP tank identity is deleted or has no verified Core target.");
            var columns = await context.Tanks.IgnoreQueryFilters().AsNoTracking().Where(x => x.NewAppId == sourceId)
                .Select(x => new { x.Id, x.IsDeleted }).Take(2).ToListAsync(cancellationToken);
            if (columns.Count > 1 || mapping is not null && columns.Any(x => x.Id != mapping.CoreId))
                throw new ConflictException("The NEWAPP tank identity conflicts with its verified Core mapping.");
            var coreId = mapping?.CoreId ?? columns.Select(x => (Guid?)x.Id).SingleOrDefault();
            if (coreId is null)
                throw new ConflictException("SourceTankId has no verified Core tank mapping. Synchronize its identity before retrying.");
            var tank = await context.Tanks.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == coreId)
                .Select(x => new { x.Id, x.IsDeleted, x.NewAppId }).SingleOrDefaultAsync(cancellationToken);
            if (tank is null || tank.IsDeleted || tank.NewAppId is not null && tank.NewAppId != sourceId)
                throw new ConflictException("The verified NEWAPP tank is missing, deleted or bound to another source ID.");
            return tank.Id;
        }

        var legacyId = Trim(dto.TankLegacyId);
        var legacyMatches = legacyId is null ? [] : await context.Tanks.AsNoTracking()
            .Where(x => x.LegacyId == legacyId).Select(x => x.Id).Take(2).ToListAsync(cancellationToken);
        var serialMatches = await MatchTankCodeAsync(dto.TankSerial, cancellationToken);
        if (legacyMatches.Count > 1 || serialMatches.Count > 1) return null;
        if (legacyMatches.Count == 1 && serialMatches.Count == 1 && legacyMatches[0] != serialMatches[0])
            throw new ConflictException("TankLegacyId and TankSerial identify different Core tanks.");
        if (legacyMatches.Count == 1) return legacyMatches[0];
        if (serialMatches.Count == 1) return serialMatches[0];
        var rfidMatches = await MatchTankCodeAsync(dto.TankRfid, cancellationToken);
        var rfid = Trim(dto.TankRfid)?.ToUpperInvariant();
        if (rfid is not null)
        {
            // Only an existing verified mapping may translate RFID. Never create ownership from a physical hint.
            var mappingQuery = context.Database.IsSqlServer()
                ? context.LegacySyncMappings.FromSqlInterpolated($"""
                SELECT m.* FROM [LegacySyncMappings] AS m
                JOIN [Tanks] AS t ON t.[Id] = m.[CoreId] AND t.[IsDeleted] = 0
                WHERE m.[Entity] = 'tank' AND m.[IsDeleted] = 0 AND m.[CoreId] IS NOT NULL
                  AND m.[SourceSystem] IN ('quanly.plasma.vn','newapp.plasmamed.vn')
                  AND UPPER(LTRIM(RTRIM(COALESCE(JSON_VALUE(m.[SnapshotJson], '$.rfid'),
                      JSON_VALUE(m.[SnapshotJson], '$.RFID'))))) = {rfid}
                """)
                : context.LegacySyncMappings.FromSqlInterpolated($"""
                SELECT m.* FROM "LegacySyncMappings" m
                JOIN "Tanks" t ON t."Id"=m."CoreId" AND NOT t."IsDeleted"
                WHERE m."Entity"='tank' AND NOT m."IsDeleted" AND m."CoreId" IS NOT NULL
                  AND m."SourceSystem" IN ('quanly.plasma.vn','newapp.plasmamed.vn')
                  AND upper(btrim(coalesce(m."SnapshotJson"->>'rfid',m."SnapshotJson"->>'RFID')))={rfid}
                """);
            var mapped = await mappingQuery.AsNoTracking().Select(x => x.CoreId!.Value)
                .Distinct().Take(2).ToListAsync(cancellationToken);
            rfidMatches = rfidMatches.Concat(mapped).Distinct().Take(2).ToList();
        }
        return rfidMatches.Count == 1 ? rfidMatches[0] : null;
    }

    private async Task<List<Guid>> MatchTankCodeAsync(string? value, CancellationToken cancellationToken)
    {
        var normalized = Trim(value)?.ToUpperInvariant();
        if (normalized is null) return [];
        return await context.Tanks.AsNoTracking().Where(x => x.TankCode.Trim().ToUpper() == normalized ||
                (x.SerialNumber != null && x.SerialNumber.Trim().ToUpper() == normalized))
            .Select(x => x.Id).Distinct().Take(2).ToListAsync(cancellationToken);
    }

    private static void ApplySession(HistorySessionIngestDto dto, Session session)
    {
        session.SourceRevision = dto.SourceRevision;
        session.StartTime = dto.StartTime.UtcDateTime;
        session.EndTime = dto.EndTime?.UtcDateTime;
        session.Mode = dto.Mode;
        session.AverageSpeed = dto.AverageSpeed;
        session.PlasmaOnTime = dto.PlasmaOnTime;
        session.EnvironmentTemperatureBefore = dto.EnvironmentTemperatureBefore;
        session.EnvironmentTemperatureAfter = dto.EnvironmentTemperatureAfter;
        session.HandleTemperatureBefore = dto.HandleTemperatureBefore;
        session.HandleTemperatureAfter = dto.HandleTemperatureAfter;
        session.ErrorCode = dto.ErrorCode;
        if (dto.UserCode is not null) session.UserCode = dto.UserCode;
    }

    private static decimal? Measurement(string? value) => HistoryMeasurement.TryParse(value, out var number)
        ? number : throw new BusinessException("A history measurement is not a plain invariant decimal.");

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static long LockKey(string identity) => BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("CorePlasma:history:" + identity)));
    private static HistoryIngestResultDto Result(HistorySessionIngestDto dto, Guid sessionId, string disposition, int upserted) => new()
    {
        EventId = dto.EventId, SourceSessionId = dto.SourceSessionId, Disposition = disposition,
        SessionId = sessionId, TankSessionsUpserted = upserted
    };
}
