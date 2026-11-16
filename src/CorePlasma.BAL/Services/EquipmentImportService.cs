using System.Text.RegularExpressions;
using CorePlasma.BAL.DTOs.Imports;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Imports;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.BAL.Services;

public sealed class EquipmentImportService(IUnitOfWork unitOfWork) : IEquipmentImportService
{
    private const int MaxReportedErrors = 200;

    // Source status code -> status of this system. Codes without an equivalent got their own enum value.
    private static readonly Dictionary<string, EngineStatus> EngineStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["READY_TO_USE"] = EngineStatus.ReadyToUse,
        ["LENT"] = EngineStatus.Lent,
        ["RENTED"] = EngineStatus.Rented,
        ["SOLD"] = EngineStatus.Sold,
        ["CHECKING"] = EngineStatus.Checking,
        ["DISABLED"] = EngineStatus.Inactive
    };

    private static readonly Dictionary<string, TankStatus> TankStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["READY_TO_USE"] = TankStatus.Available,
        ["AT_CLIENT"] = TankStatus.AtClient,
        ["LOADING"] = TankStatus.Loading,
        ["EMPTY"] = TankStatus.Empty,
        ["LOCKED"] = TankStatus.Locked,
        ["DISABLED"] = TankStatus.Inactive
    };

    public Task<ImportResultDto> ImportEnginesAsync(Stream excel, CancellationToken cancellationToken) =>
        ImportAsync(
            excel,
            ["serial", "equipmentIdx", "_id"],
            unitOfWork.Engines,
            x => x.LegacyId,
            x => x.EngineCode,
            ApplyEngine,
            cancellationToken);

    public Task<ImportResultDto> ImportTanksAsync(Stream excel, CancellationToken cancellationToken) =>
        ImportAsync(
            excel,
            ["serial", "tankIdx", "_id"],
            unitOfWork.Tanks,
            x => x.LegacyId,
            x => x.TankCode,
            ApplyTank,
            cancellationToken);

    private static void ApplyEngine(LegacyRow row, Engine engine, bool isNew)
    {
        var serial = Required(row, "serial", 50);
        engine.EngineCode = serial;
        engine.SerialNumber = serial;
        engine.EngineName = Required(row, "name", 250);
        engine.Status = MapStatus(row, EngineStatuses);
        engine.EngineType = Optional(row, "type", 30);
        engine.TankType = Optional(row, "tankType", 20);
        engine.Store = Optional(row, "store", 10);
        engine.Description = Optional(row, "note", 1000);
        engine.HasWarranty = row.YesNo("warrantyOption");
        engine.WarrantyStartDate = row.UtcDate("warrantyStartDate");
        engine.HasMaintenance = row.YesNo("maintainOption");
        engine.LastMaintenanceDate = row.UtcDate("maintainedAt");
        engine.LegacyId = Required(row, "_id", 50);
        engine.EquipmentIndex = row.Integer("equipmentIdx");
        ApplySourceDates(row, engine, isNew);
    }

    private static void ApplyTank(LegacyRow row, Tank tank, bool isNew)
    {
        var serial = Required(row, "serial", 50);
        // A later source import must not re-pad a serial deliberately corrected in Core_Plasma.
        if (isNew || !ShouldPreserveUnpaddedIdentifier(tank.TankCode, serial))
        {
            tank.TankCode = serial;
        }

        if (isNew || !ShouldPreserveUnpaddedIdentifier(tank.SerialNumber, serial))
        {
            tank.SerialNumber = serial;
        }
        tank.Status = MapStatus(row, TankStatuses);
        tank.TankType = Optional(row, "type", 100);
        tank.Store = Optional(row, "store", 10);
        tank.LegacyInvoiceId = Optional(row, "invoiceId", 50);
        tank.Description = Optional(row, "note", 1000);
        tank.LegacyId = Required(row, "_id", 50);
        tank.TankIndex = row.Integer("tankIdx");
        ApplySourceDates(row, tank, isNew);
    }

    private static bool ShouldPreserveUnpaddedIdentifier(string? current, string incoming)
    {
        if (current is null || current.Length >= incoming.Length)
        {
            return false;
        }

        var currentMatch = Regex.Match(current, @"\A([A-Za-z]+)([0-9]+)\z", RegexOptions.CultureInvariant);
        var incomingMatch = Regex.Match(incoming, @"\A([A-Za-z]+)([0-9]+)\z", RegexOptions.CultureInvariant);
        return currentMatch.Success && incomingMatch.Success
            && string.Equals(currentMatch.Groups[1].Value, incomingMatch.Groups[1].Value, StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentMatch.Groups[2].Value.TrimStart('0'), incomingMatch.Groups[2].Value.TrimStart('0'), StringComparison.Ordinal);
    }

    /// <summary>New records keep the source timestamps; updates get the import time from the interceptor.</summary>
    private static void ApplySourceDates(LegacyRow row, BaseEntity entity, bool isNew)
    {
        if (!isNew)
        {
            return;
        }

        entity.CreatedDate = row.UtcDate("createdAt") ?? default;
        entity.UpdatedDate = row.UtcDate("updatedAt");
    }

    private async Task<ImportResultDto> ImportAsync<T>(
        Stream excel,
        string[] requiredColumns,
        IRepository<T> repository,
        Func<T, string?> legacyIdOf,
        Func<T, string> codeOf,
        Action<LegacyRow, T, bool> apply,
        CancellationToken cancellationToken)
        where T : BaseEntity, new()
    {
        var (sheetName, rows) = LegacySheetReader.Read(excel, requiredColumns);

        var existing = await repository.Query().ToListAsync(cancellationToken);
        var byLegacyId = existing.Where(x => legacyIdOf(x) is not null).ToDictionary(x => legacyIdOf(x)!, StringComparer.Ordinal);
        var byCode = existing.ToDictionary(codeOf, StringComparer.Ordinal);

        var seenLegacyIds = new HashSet<string>(StringComparer.Ordinal);
        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        var toAdd = new List<T>();
        var errors = new List<string>();
        var updated = 0;

        foreach (var row in rows)
        {
            try
            {
                var legacyId = Required(row, "_id", 50);
                var code = Required(row, "serial", 50);

                if (!seenLegacyIds.Add(legacyId))
                {
                    throw new FormatException($"_id '{legacyId}' appears more than once in the file.");
                }

                if (!seenCodes.Add(code))
                {
                    throw new FormatException($"serial '{code}' appears more than once in the file.");
                }

                var target = byLegacyId.GetValueOrDefault(legacyId);
                var sameCode = byCode.GetValueOrDefault(code);
                if (target is null && sameCode is not null && legacyIdOf(sameCode) is not null)
                {
                    throw new FormatException($"serial '{code}' already belongs to another imported record ({legacyIdOf(sameCode)}).");
                }

                if (target is not null && sameCode is not null && !ReferenceEquals(target, sameCode))
                {
                    throw new FormatException($"serial '{code}' is already used by another record.");
                }

                target ??= sameCode;
                var entity = target ?? new T();
                apply(row, entity, target is null);

                if (target is null)
                {
                    toAdd.Add(entity);
                }
                else
                {
                    updated++;
                }
            }
            catch (FormatException ex)
            {
                errors.Add($"{sheetName} row {row.RowNumber}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            throw new BusinessException(
                $"Import rejected: {errors.Count} invalid row(s) in sheet '{sheetName}'. Nothing was saved.",
                errors.Take(MaxReportedErrors).ToList());
        }

        await repository.AddRangeAsync(toAdd, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ImportResultDto
        {
            SheetName = sheetName,
            TotalRows = rows.Count,
            Created = toAdd.Count,
            Updated = updated
        };
    }

    private static string Required(LegacyRow row, string column, int maxLength) =>
        Optional(row, column, maxLength) ?? throw new FormatException($"'{column}' is required.");

    private static string? Optional(LegacyRow row, string column, int maxLength)
    {
        var text = row.Text(column);
        return text is not null && text.Length > maxLength
            ? throw new FormatException($"'{column}' is longer than {maxLength} characters.")
            : text;
    }

    private static TStatus MapStatus<TStatus>(LegacyRow row, Dictionary<string, TStatus> map)
    {
        var code = row.Text("status") ?? throw new FormatException("'status' is required.");
        return map.TryGetValue(code, out var status)
            ? status
            : throw new FormatException($"Unknown status '{code}'. Expected: {string.Join(", ", map.Keys)}.");
    }
}
