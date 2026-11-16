namespace CorePlasma.BAL.DTOs.Tanks;

public class TankDetailDto : TankDto
{
    public string? Manufacturer { get; set; }

    public DateTime? ManufactureDate { get; set; }

    public DateTime? InspectionDate { get; set; }

    public decimal? WorkingPressure { get; set; }

    public string? CustomerCode { get; set; }

    public string? AssignedEmployeeCode { get; set; }

    public string? Description { get; set; }

    public string? LegacyId { get; set; }

    public int? TankIndex { get; set; }

    public string? LegacyInvoiceId { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public Guid? UpdatedBy { get; set; }
}
