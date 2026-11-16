using CorePlasma.Common.Enums;
using CorePlasma.Common.Models;

namespace CorePlasma.BAL.DTOs.Engines;

public sealed class EngineFilterDto : PagedRequest
{
    public string? Department { get; set; }

    public string? CurrentLocation { get; set; }

    public Guid? ResponsibleEmployeeId { get; set; }

    public EngineStatus? Status { get; set; }

    public string? EngineType { get; set; }

    public string? TankType { get; set; }

    public string? Store { get; set; }
}
