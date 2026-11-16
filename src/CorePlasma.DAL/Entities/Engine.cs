using CorePlasma.Common.Enums;

namespace CorePlasma.DAL.Entities;

/// <summary>
/// Máy / Máy Plasma.
/// </summary>
public class Engine : BaseEntity, IHasDeletedStatus, ISourceIdentified
{
    /// <summary>Representative numeric source ID; all verified aliases remain in LegacySyncMappings.</summary>
    public long? QuanLyId { get; set; }

    public long? NewAppId { get; set; }

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

    /// <summary>Machine type of the source system: HOSPITAL, CLINIC_BIG, CLINIC_MEDIUM, PLASMA_CARE, OTHER.</summary>
    public string? EngineType { get; set; }

    /// <summary>Tank type the machine uses: TANK_8L, TANK_4L, TANK_3LC.</summary>
    public string? TankType { get; set; }

    /// <summary>Warehouse code: HN, SG.</summary>
    public string? Store { get; set; }

    public bool? HasWarranty { get; set; }

    public DateTime? WarrantyStartDate { get; set; }

    public bool? HasMaintenance { get; set; }

    /// <summary>Record id (_id) in the PlasmaMED source system; key for re-imports.</summary>
    public string? LegacyId { get; set; }

    /// <summary>equipmentIdx of the source system.</summary>
    public int? EquipmentIndex { get; set; }

    public Employee? ResponsibleEmployee { get; set; }

    public ICollection<Session> Sessions { get; set; } = new List<Session>();

    public void MarkAsDeleted() => Status = EngineStatus.Deleted;
}
