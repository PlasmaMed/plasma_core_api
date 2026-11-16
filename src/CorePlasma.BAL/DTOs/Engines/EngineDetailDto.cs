namespace CorePlasma.BAL.DTOs.Engines;

public class EngineDetailDto : EngineDto
{
    public int? ManufactureYear { get; set; }

    public DateTime? PurchaseDate { get; set; }

    public DateTime? InstallationDate { get; set; }

    public decimal? Power { get; set; }

    public decimal? Voltage { get; set; }

    public decimal? Current { get; set; }

    public decimal? CuttingThickness { get; set; }

    public DateTime? LastMaintenanceDate { get; set; }

    public string? ResponsibleEmployeeCode { get; set; }

    public string? Description { get; set; }

    public bool? HasWarranty { get; set; }

    public DateTime? WarrantyStartDate { get; set; }

    public bool? HasMaintenance { get; set; }

    public string? LegacyId { get; set; }

    public int? EquipmentIndex { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public Guid? UpdatedBy { get; set; }
}
