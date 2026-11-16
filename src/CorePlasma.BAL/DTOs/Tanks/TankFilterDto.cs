using CorePlasma.Common.Enums;
using CorePlasma.Common.Models;

namespace CorePlasma.BAL.DTOs.Tanks;

public sealed class TankFilterDto : PagedRequest
{
    public string? TankCode { get; set; }

    public string? SerialNumber { get; set; }

    public string? GasType { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? AssignedEmployeeId { get; set; }

    public string? CurrentLocation { get; set; }

    public TankStatus? Status { get; set; }

    public string? TankType { get; set; }

    public string? Store { get; set; }

    /// <summary>Gas warning of the tank in the machine at the customer: "Low", "Empty" or "Any" (both).</summary>
    public string? GasWarning { get; set; }
}
