using CorePlasma.Common.Enums;

namespace CorePlasma.BAL.DTOs.Engines;

public abstract class EngineWriteDto
{
    /// <summary>
    /// Leave empty on create to have the system generate it (DocumentSequences).
    /// </summary>
    public string EngineCode { get; set; } = string.Empty;

    public string EngineName { get; set; } = string.Empty;

    public string? SerialNumber { get; set; }

    public string? Model { get; set; }

    public string? Manufacturer { get; set; }

    public int? ManufactureYear { get; set; }

    public DateTime? PurchaseDate { get; set; }

    public DateTime? InstallationDate { get; set; }

    public decimal? Power { get; set; }

    public decimal? Voltage { get; set; }

    public decimal? Current { get; set; }

    public decimal? CuttingThickness { get; set; }

    public string? CurrentLocation { get; set; }

    public string? Department { get; set; }

    public Guid? ResponsibleEmployeeId { get; set; }

    public EngineStatus Status { get; set; } = EngineStatus.Active;

    public DateTime? LastMaintenanceDate { get; set; }

    public DateTime? NextMaintenanceDate { get; set; }

    public string? Description { get; set; }

    public string? EngineType { get; set; }

    public string? TankType { get; set; }

    public string? Store { get; set; }

    public bool? HasWarranty { get; set; }

    public DateTime? WarrantyStartDate { get; set; }

    public bool? HasMaintenance { get; set; }
}

public sealed class CreateEngineDto : EngineWriteDto;

public sealed class UpdateEngineDto : EngineWriteDto;
