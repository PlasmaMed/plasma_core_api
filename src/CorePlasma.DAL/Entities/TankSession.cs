namespace CorePlasma.DAL.Entities;

/// <summary>
/// Một bình khí được dùng trong một phiên máy: mã RFID của bình, áp suất và cân nặng (load cell) trước/sau phiên.
/// Units are not defined by the source and are stored without conversion.
/// </summary>
public class TankSession : BaseEntity
{
    public Guid SessionId { get; set; }

    /// <summary>RFID code as recorded in the session (null when the machine did not read a tag).</summary>
    public string? TankRfid { get; set; }

    /// <summary>Set only when the RFID identifies exactly one tank.</summary>
    public Guid? TankId { get; set; }

    /// <summary>Tank slot order on the machine (source "Order").</summary>
    public int? Order { get; set; }

    /// <summary>High pressure before use. NEWAPP sends it as "HighPressureAfter" (its Before/After labels are swapped).</summary>
    public decimal? PressureBefore { get; set; }

    /// <summary>High pressure after use (NEWAPP "HighPressureBefore").</summary>
    public decimal? PressureAfter { get; set; }

    /// <summary>Load cell before use (NEWAPP "LoadCellAfter", labels swapped like the pressure).</summary>
    public decimal? WeightBefore { get; set; }

    /// <summary>Load cell after use (NEWAPP "LoadCellBefore").</summary>
    public decimal? WeightAfter { get; set; }

    /// <summary>TankSession.Id in NEWAPP; key for re-imports.</summary>
    public long? LegacyId { get; set; }

    public Session Session { get; set; } = null!;

    public Tank? Tank { get; set; }
}
