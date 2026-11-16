using Microsoft.EntityFrameworkCore;

// Minimal source graph for testing the production enqueuer/store without loading legacy startup or its full schema.
namespace PlasmaVN.Infrastructure.Entities
{
    public sealed class Engine { public long Id { get; set; } public string Serial { get; set; } = ""; public bool IsDeleted { get; set; } }
    public sealed class Tank { public long Id { get; set; } public string Serial { get; set; } = ""; public string? OldId { get; set; } public bool IsDeleted { get; set; } }
    public sealed class Session
    {
        public long Id { get; set; } public long EngineId { get; set; }
        public DateTime StartTime { get; set; } public DateTime EndTime { get; set; }
        public string? Mode { get; set; } public string? AverageSpeed { get; set; } public string? PlasmaOnTime { get; set; }
        public string? EnviromentTemperatureBefore { get; set; } public string? EnviromentTemperatureAfter { get; set; }
        public string? HandleTemperatureBefore { get; set; } public string? HandleTemperatureAfter { get; set; } public string? ErrorCode { get; set; }
        public string? UserCode { get; set; }
        public List<TankSession> TankSessions { get; set; } = [];
    }
    public sealed class TankSession
    {
        public long Id { get; set; } public long SessionId { get; set; } public long? TankId { get; set; } public int? Order { get; set; }
        public string? RFID { get; set; } public string? HighPressureBefore { get; set; } public string? HighPressureAfter { get; set; }
        public string? LoadCellBefore { get; set; } public string? LoadCellAfter { get; set; }
        public Session Session { get; set; } = null!;
    }
}

namespace PlasmaVN.Infrastructure
{
    public class PlasmaContext : DbContext
    {
        public PlasmaContext(DbContextOptions<PlasmaContext> options) : base(options) { }
        protected PlasmaContext(DbContextOptions options) : base(options) { }
        public DbSet<Entities.Session> Sessions => Set<Entities.Session>();
        public DbSet<Entities.TankSession> TankSessions => Set<Entities.TankSession>();
        public DbSet<Entities.Engine> Engines => Set<Entities.Engine>();
        public DbSet<Entities.Tank> Tanks => Set<Entities.Tank>();
        public DbSet<Entities.CoreHistoryOutbox> CoreHistoryOutbox => Set<Entities.CoreHistoryOutbox>();

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Entities.Engine>().ToTable("Engine"); model.Entity<Entities.Engine>().Property(row => row.Id).ValueGeneratedNever();
            model.Entity<Entities.Engine>().HasQueryFilter(row => !row.IsDeleted);
            model.Entity<Entities.Tank>().ToTable("Tank"); model.Entity<Entities.Tank>().Property(row => row.Id).ValueGeneratedNever();
            model.Entity<Entities.Tank>().HasQueryFilter(row => !row.IsDeleted);
            model.Entity<Entities.Session>().ToTable("Session"); model.Entity<Entities.Session>().Property(row => row.Id).ValueGeneratedNever();
            model.Entity<Entities.Session>().Property(row => row.StartTime).HasColumnType("timestamp without time zone");
            model.Entity<Entities.Session>().Property(row => row.EndTime).HasColumnType("timestamp without time zone");
            model.Entity<Entities.TankSession>().ToTable("TankSession"); model.Entity<Entities.TankSession>().Property(row => row.Id).ValueGeneratedNever();
            model.Entity<Entities.TankSession>().HasOne(row => row.Session).WithMany(row => row.TankSessions).HasForeignKey(row => row.SessionId);
            var queue = model.Entity<Entities.CoreHistoryOutbox>(); queue.ToTable("CoreHistoryOutbox", "public");
            queue.Property(row => row.Id).ValueGeneratedNever(); queue.Property(row => row.PayloadJson).IsRequired();
            queue.Property(row => row.CreatedUtc).HasColumnType("timestamp without time zone");
            queue.Property(row => row.DeliveredUtc).HasColumnType("timestamp without time zone");
            queue.Property(row => row.NextAttemptUtc).HasColumnType("timestamp without time zone");
            queue.Property(row => row.LeaseUntilUtc).HasColumnType("timestamp without time zone");
            queue.Property(row => row.LastErrorCode).IsRequired(false);
        }
    }

    public sealed class SourceGraphContext(DbContextOptions<SourceGraphContext> options) : PlasmaContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) { base.OnModelCreating(model); model.Ignore<Entities.CoreHistoryOutbox>(); }
    }
}
