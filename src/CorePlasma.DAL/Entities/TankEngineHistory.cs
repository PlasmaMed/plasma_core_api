namespace CorePlasma.DAL.Entities;

/// <summary>
/// Sự kiện bình–máy: máy đọc được mã RFID của một bình tại một thời điểm.
/// EventType keeps the source code (0/1); its meaning is not documented by the source.
/// </summary>
public class TankEngineHistory : BaseEntity
{
    public Guid EngineId { get; set; }

    /// <summary>RFID tag code; a few source rows contain the raw device frame instead (kept as is).</summary>
    public string? TankRfid { get; set; }

    /// <summary>Set only when the RFID identifies exactly one tank.</summary>
    public Guid? TankId { get; set; }

    /// <summary>UTC (source "CreateTime").</summary>
    public DateTime EventTime { get; set; }

    /// <summary>Source "Type" code.</summary>
    public int EventType { get; set; }

    /// <summary>TankEngineHistory.Id in NEWAPP; key for re-imports.</summary>
    public long? LegacyId { get; set; }

    public Engine Engine { get; set; } = null!;

    public Tank? Tank { get; set; }
}
