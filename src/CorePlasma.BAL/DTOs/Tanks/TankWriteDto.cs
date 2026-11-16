using CorePlasma.Common.Enums;

namespace CorePlasma.BAL.DTOs.Tanks;

public abstract class TankWriteDto
{
    /// <summary>
    /// Leave empty on create to have the system generate it (DocumentSequences).
    /// </summary>
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

    public string? Store { get; set; }
}

public sealed class CreateTankDto : TankWriteDto;

public sealed class UpdateTankDto : TankWriteDto;
