namespace CorePlasma.DAL.Entities;

/// <summary>
/// Phiên sử dụng máy: một lần chạy của máy (Engine) từ lúc bắt đầu đến lúc kết thúc.
/// Lookup path: TankSession.SessionId → Session.Id → Session.EngineId → Engine.Id.
/// Measurement fields are kept as the source text (NEWAPP); their units are not defined by the source.
/// </summary>
public class Session : BaseEntity
{
    public Guid EngineId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime StartTime { get; set; }

    /// <summary>UTC. Null while the session is still running. Imported data may end before it starts (kept as is).</summary>
    public DateTime? EndTime { get; set; }

    public string? Mode { get; set; }

    public string? AverageSpeed { get; set; }

    public string? PlasmaOnTime { get; set; }

    public string? EnvironmentTemperatureBefore { get; set; }

    public string? EnvironmentTemperatureAfter { get; set; }

    public string? HandleTemperatureBefore { get; set; }

    public string? HandleTemperatureAfter { get; set; }

    public string? ErrorCode { get; set; }

    public string? UserCode { get; set; }

    /// <summary>Session.Id in NEWAPP (newapp.plasmamed.com.vn); key for re-imports.</summary>
    public long? LegacyId { get; set; }

    /// <summary>Last committed NEWAPP outbox revision; imported snapshots start at zero.</summary>
    public long SourceRevision { get; set; }

    public Engine Engine { get; set; } = null!;

    public ICollection<TankSession> TankSessions { get; set; } = new List<TankSession>();
}
