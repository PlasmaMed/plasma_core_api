using CorePlasma.Common.Models;

namespace CorePlasma.BAL.DTOs.Sessions;

/// <summary>Phiên sử dụng máy kèm các bình khí đã dùng trong phiên.</summary>
public sealed class SessionDto
{
    public Guid Id { get; set; }

    public Guid EngineId { get; set; }

    public string EngineCode { get; set; } = string.Empty;

    /// <summary>Engine.SerialNumber — mã máy dùng để tra cứu.</summary>
    public string? EngineSerial { get; set; }

    public string EngineName { get; set; } = string.Empty;

    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public string? Mode { get; set; }

    public string? AverageSpeed { get; set; }

    public string? PlasmaOnTime { get; set; }

    public string? EnvironmentTemperatureBefore { get; set; }

    public string? EnvironmentTemperatureAfter { get; set; }

    public string? HandleTemperatureBefore { get; set; }

    public string? HandleTemperatureAfter { get; set; }

    public string? ErrorCode { get; set; }

    public string? UserCode { get; set; }

    public long? LegacyId { get; set; }

    /// <summary>Null while the session is still running.</summary>
    public double? DurationMinutes => EndTime.HasValue ? Math.Round((EndTime.Value - StartTime).TotalMinutes, 2) : null;

    /// <summary>Device clock problem in Start/EndTime (shown as a red row), or null when the times look valid.</summary>
    public string? TimeIssue => SessionTimeCheck.Describe(StartTime, EndTime, DateTime.UtcNow);

    public List<TankSessionDto> Tanks { get; set; } = [];
}

public sealed class TankSessionDto
{
    public Guid Id { get; set; }

    public string? TankRfid { get; set; }

    /// <summary>Tank resolved from the RFID (null when unknown or ambiguous).</summary>
    public Guid? TankId { get; set; }

    public string? TankCode { get; set; }

    public int? Order { get; set; }

    public decimal? PressureBefore { get; set; }

    public decimal? PressureAfter { get; set; }

    /// <summary>PressureBefore − PressureAfter.</summary>
    public decimal? PressureUsed => PressureBefore - PressureAfter;

    /// <summary>Raw high-pressure sensor value per 1 % (12 500 = 100 %), as displayed by NEWAPP.</summary>
    public const decimal PressureUnitsPerPercent = 125m;

    /// <summary>Round(PressureBefore / 125) — "CB Cao áp Trước (%)".</summary>
    public int? PressureBeforePercent => ToPercent(PressureBefore);

    /// <summary>Round(PressureAfter / 125) — "CB Cao áp Sau (%)".</summary>
    public int? PressureAfterPercent => ToPercent(PressureAfter);

    public static int? ToPercent(decimal? pressure) =>
        pressure.HasValue ? (int)Math.Round(pressure.Value / PressureUnitsPerPercent, MidpointRounding.AwayFromZero) : null;

    public decimal? WeightBefore { get; set; }

    public decimal? WeightAfter { get; set; }

    /// <summary>WeightBefore − WeightAfter.</summary>
    public decimal? WeightUsed => WeightBefore - WeightAfter;
}

public sealed class SessionFilterDto : PagedRequest
{
    public Guid? EngineId { get; set; }

    /// <summary>Engine serial (or engine code), whole value, case-insensitive.</summary>
    public string? Serial { get; set; }

    /// <summary>Sessions that used this tank RFID, whole value, case-insensitive.</summary>
    public string? TankRfid { get; set; }

    /// <summary>Sessions that used this tank (TankSession.TankId, resolved from the RFID at import).</summary>
    public Guid? TankId { get; set; }

    /// <summary>Sessions starting at or after this time (UTC).</summary>
    public DateTime? FromDate { get; set; }

    /// <summary>Sessions starting at or before this time (UTC).</summary>
    public DateTime? ToDate { get; set; }
}

/// <summary>Sự kiện bình–máy (máy đọc RFID bình).</summary>
public sealed class TankEngineHistoryDto
{
    public Guid Id { get; set; }

    public Guid EngineId { get; set; }

    public string EngineCode { get; set; } = string.Empty;

    public string? EngineSerial { get; set; }

    public string? TankRfid { get; set; }

    public Guid? TankId { get; set; }

    public string? TankCode { get; set; }

    public DateTime EventTime { get; set; }

    /// <summary>Source code (0/1).</summary>
    public int EventType { get; set; }
}

public sealed class TankEngineHistoryFilterDto : PagedRequest
{
    public Guid? EngineId { get; set; }

    public string? Serial { get; set; }

    public string? TankRfid { get; set; }

    public int? EventType { get; set; }

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }
}

/// <summary>
/// Plausibility of session times recorded by the machine clock (imported data keeps them as recorded).
/// Real data starts in 2018; sessions last minutes, so more than a day is a clock error.
/// </summary>
public static class SessionTimeCheck
{
    public static readonly DateTime EarliestValid = new(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);

    /// <summary>Allowed clock drift into the future.</summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(10);

    public static string? Describe(DateTime start, DateTime? end, DateTime nowUtc)
    {
        if (end < start)
        {
            return "Thời gian kết thúc trước thời gian bắt đầu";
        }

        if (start < EarliestValid || end < EarliestValid)
        {
            return $"Mốc thời gian không hợp lệ (năm {Math.Min(start.Year, end?.Year ?? start.Year)})";
        }

        if (start > nowUtc + FutureTolerance || end > nowUtc + FutureTolerance)
        {
            return "Mốc thời gian ở tương lai";
        }

        if (end is { } finish && finish - start > MaxDuration)
        {
            return $"Phiên kéo dài bất thường ({(finish - start).TotalDays:0.#} ngày)";
        }

        return null;
    }
}
