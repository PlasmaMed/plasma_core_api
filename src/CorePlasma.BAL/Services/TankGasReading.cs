using CorePlasma.BAL.DTOs.Sessions;
using CorePlasma.BAL.DTOs.Tanks;
using CorePlasma.Common.Enums;

namespace CorePlasma.BAL.Services;

/// <summary>
/// The tank's latest usage in the session history (TankSession → Session → Engine), the same data as the
/// machine/tank history tables. No physical inventory fields are changed.
/// </summary>
internal sealed class TankGasReading
{
    public Guid TankId { get; init; }
    public Guid EngineId { get; init; }
    public string EngineCode { get; init; } = string.Empty;
    public Guid? ResponsibleEmployeeId { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public decimal? PressureBefore { get; init; }
    public decimal? PressureAfter { get; init; }
    public bool IsLatestEngineSession { get; init; }

    /// <summary>
    /// Low/empty warnings only for a tank that is still in the machine at the customer: its latest usage is the
    /// machine's latest session and the tank is at the customer / in use. A tank taken out or back in stock is not
    /// warned about (its last reading is still shown for reference).
    /// </summary>
    public static bool CanWarn(TankDto tank, bool isLatestEngineSession) =>
        isLatestEngineSession && tank.Status is TankStatus.AtClient or TankStatus.InUse;

    public void ApplyTo(TankDto tank, Guid? restrictedEmployeeId)
    {
        // A user restricted to his own machines does not see readings taken on other machines.
        if (restrictedEmployeeId.HasValue && ResponsibleEmployeeId != restrictedEmployeeId)
        {
            return;
        }

        // While a session is still running only the reading taken before use exists.
        var pressure = EndTime.HasValue ? PressureAfter : PressureBefore;
        if (pressure is not >= 0m)
        {
            return;
        }

        var percent = pressure.Value / TankSessionDto.PressureUnitsPerPercent;
        tank.CurrentEngineId = EngineId;
        tank.CurrentEngineCode = EngineCode;
        tank.IsCurrentlyUsed = IsLatestEngineSession;
        tank.GasReadingTime = EndTime ?? StartTime;
        tank.GasRemainingPercent = percent;
        tank.GasUsedPercent = EndTime.HasValue && PressureBefore.HasValue && PressureAfter.HasValue
            ? (PressureBefore.Value - PressureAfter.Value) / TankSessionDto.PressureUnitsPerPercent
            : null;
        tank.GasWarning = !CanWarn(tank, IsLatestEngineSession) ? null
            : percent == 0m ? "Empty"
            : percent < 10m ? "Low"
            : null;
    }
}
