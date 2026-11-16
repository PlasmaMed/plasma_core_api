using CorePlasma.Common.Enums;
using CorePlasma.BAL.DTOs.Integration;

namespace CorePlasma.BAL.DTOs.Tanks;

public class TankDto
{
    /// <summary>Read-only source identifiers. Core Id is independent.</summary>
    public long? QuanLyId { get; set; }

    public long? NewAppId { get; set; }

    public IReadOnlyList<LegacySourceIdentityDto> SourceIdentities { get; set; } = [];

    public Guid Id { get; set; }

    public string TankCode { get; set; } = string.Empty;

    public string? SerialNumber { get; set; }

    public string? TankType { get; set; }

    public string? GasType { get; set; }

    public decimal? Capacity { get; set; }

    public decimal? CurrentPressure { get; set; }

    public string? Unit { get; set; }

    public TankStatus Status { get; set; }

    public string? CurrentLocation { get; set; }

    public string? Store { get; set; }

    public DateTime? NextInspectionDate { get; set; }

    public Guid? CustomerId { get; set; }

    public string? CustomerName { get; set; }

    public Guid? AssignedEmployeeId { get; set; }

    public string? AssignedEmployeeName { get; set; }

    public DateTime CreatedDate { get; set; }

    /// <summary>The latest usage in the history is still the machine's latest recorded session.</summary>
    public bool IsCurrentlyUsed { get; set; }

    public Guid? CurrentEngineId { get; set; }

    public string? CurrentEngineCode { get; set; }

    /// <summary>
    /// Gas left after the tank's latest usage in the session history (pressure after use / 125, unrounded,
    /// same scale as the history table); null when the tank has no usable reading.
    /// </summary>
    public decimal? GasRemainingPercent { get; set; }

    /// <summary>Gas used during that latest session (before − after, in %); null when unknown.</summary>
    public decimal? GasUsedPercent { get; set; }

    /// <summary>Empty at exactly 0%; Low above 0% and below 10%; otherwise null.</summary>
    public string? GasWarning { get; set; }

    /// <summary>Time of the recorded pressure measurement, not a live telemetry timestamp.</summary>
    public DateTime? GasReadingTime { get; set; }
}
