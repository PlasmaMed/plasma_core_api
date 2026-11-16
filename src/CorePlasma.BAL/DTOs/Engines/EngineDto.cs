using CorePlasma.Common.Enums;

namespace CorePlasma.BAL.DTOs.Engines;

public class EngineDto
{
    /// <summary>Read-only source identifiers. Core Id is independent.</summary>
    public long? QuanLyId { get; set; }

    public long? NewAppId { get; set; }

    public Guid Id { get; set; }

    public string EngineCode { get; set; } = string.Empty;

    public string EngineName { get; set; } = string.Empty;

    public string? SerialNumber { get; set; }

    public string? Model { get; set; }

    public string? Manufacturer { get; set; }

    public string? CurrentLocation { get; set; }

    public string? Department { get; set; }

    public EngineStatus Status { get; set; }

    /// <summary>A completed usage session has a tank pressure drop greater than 6 percentage points.</summary>
    public bool HasPressureDropWarning { get; set; }

    /// <summary>Device faults reported at least three times during the current Vietnam calendar day.</summary>
    public IReadOnlyList<EngineErrorWarningDto> RecurringErrorWarnings { get; set; } = [];

    public string? EngineType { get; set; }

    public string? TankType { get; set; }

    public string? Store { get; set; }

    public DateTime? NextMaintenanceDate { get; set; }

    public Guid? ResponsibleEmployeeId { get; set; }

    public string? ResponsibleEmployeeName { get; set; }

    public DateTime CreatedDate { get; set; }
}
