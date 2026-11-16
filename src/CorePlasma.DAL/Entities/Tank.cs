using CorePlasma.Common.Enums;

namespace CorePlasma.DAL.Entities;

/// <summary>
/// Bình khí.
/// </summary>
public class Tank : BaseEntity, IHasDeletedStatus, ISourceIdentified
{
    /// <summary>Representative numeric source ID; all verified aliases remain in LegacySyncMappings.</summary>
    public long? QuanLyId { get; set; }

    public long? NewAppId { get; set; }

    public string TankCode { get; set; } = string.Empty;

    public string? SerialNumber { get; set; }

    public string? TankType { get; set; }

    public string? GasType { get; set; }

    public string? Manufacturer { get; set; }

    public DateTime? ManufactureDate { get; set; }

    public DateTime? InspectionDate { get; set; }

    public DateTime? NextInspectionDate { get; set; }

    public decimal? Capacity { get; set; }

    public decimal? WorkingPressure { get; set; }

    public decimal? CurrentPressure { get; set; }

    public string? Unit { get; set; }

    public TankStatus Status { get; set; } = TankStatus.Available;

    public string? CurrentLocation { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? AssignedEmployeeId { get; set; }

    public string? Description { get; set; }

    /// <summary>Warehouse code: HN, SG.</summary>
    public string? Store { get; set; }

    /// <summary>Record id (_id) in the PlasmaMED source system; key for re-imports.</summary>
    public string? LegacyId { get; set; }

    /// <summary>tankIdx of the source system.</summary>
    public int? TankIndex { get; set; }

    /// <summary>invoiceId of the source system.</summary>
    public string? LegacyInvoiceId { get; set; }

    public Customer? Customer { get; set; }

    public Employee? AssignedEmployee { get; set; }

    public void MarkAsDeleted() => Status = TankStatus.Deleted;
}
