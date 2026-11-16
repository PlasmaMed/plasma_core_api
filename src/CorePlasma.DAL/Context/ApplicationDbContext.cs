using System.Linq.Expressions;
using System.Text.RegularExpressions;
using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.DAL.Context;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<Tank> Tanks => Set<Tank>();

    public DbSet<Engine> Engines => Set<Engine>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<EmployeePermission> EmployeePermissions => Set<EmployeePermission>();

    public DbSet<PermissionAuditLog> PermissionAuditLogs => Set<PermissionAuditLog>();

    public DbSet<DataChangeLog> DataChangeLogs => Set<DataChangeLog>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<TankSession> TankSessions => Set<TankSession>();

    public DbSet<TankEngineHistory> TankEngineHistories => Set<TankEngineHistory>();

    public DbSet<HistoryIngestReceipt> HistoryIngestReceipts => Set<HistoryIngestReceipt>();

    public DbSet<LegacySyncMapping> LegacySyncMappings => Set<LegacySyncMapping>();

    public DbSet<LegacySyncReceipt> LegacySyncReceipts => Set<LegacySyncReceipt>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderTank> OrderTanks => Set<OrderTank>();

    public DbSet<EmployeeUser> EmployeeUsers => Set<EmployeeUser>();

    public DbSet<CustomerUser> CustomerUsers => Set<CustomerUser>();

    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();

    public DbSet<TankTransfer> TankTransfers => Set<TankTransfer>();

    public DbSet<TankTransferItem> TankTransferItems => Set<TankTransferItem>();

    public DbSet<CoreBusinessRecord> CoreBusinessRecords => Set<CoreBusinessRecord>();
    public DbSet<CoreBusinessProjectionState> CoreBusinessProjectionStates => Set<CoreBusinessProjectionState>();

    public const string SoftDeleteFilter = "SoftDelete";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        if (Database.IsNpgsql())
        {
            // Keyword search ignores Vietnamese diacritics (see SearchPattern).
            modelBuilder.HasPostgresExtension("unaccent");
        }

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        if (Database.IsSqlServer())
        {
            // These imported PostgreSQL jsonb columns are nvarchar(max) in SQL Server.
            modelBuilder.Entity<DataChangeLog>().Property(x => x.Changes).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<LegacySyncMapping>().Property(x => x.SnapshotJson).HasColumnType("nvarchar(max)");
            modelBuilder.Entity<LegacySyncReceipt>().Property(x => x.SnapshotJson).HasColumnType("nvarchar(max)");

            // The isolated SQL Server test database stores these measurements as decimal(38,18).
            modelBuilder.Entity<TankSession>().Property(x => x.PressureBefore).HasColumnType("decimal(38,18)");
            modelBuilder.Entity<TankSession>().Property(x => x.PressureAfter).HasColumnType("decimal(38,18)");
            modelBuilder.Entity<TankSession>().Property(x => x.WeightBefore).HasColumnType("decimal(38,18)");
            modelBuilder.Entity<TankSession>().Property(x => x.WeightAfter).HasColumnType("decimal(38,18)");

            foreach (var index in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetIndexes()))
            {
                if (index.GetFilter() is { } filter)
                {
                    index.SetFilter(Regex.Replace(filter, "\"([^\"]+)\"", "[$1]").Replace("false", "0", StringComparison.Ordinal));
                }
            }
        }

        ApplySoftDeleteQueryFilter(modelBuilder);
    }

    /// <summary>
    /// Adds "IsDeleted = false" to every BaseEntity so normal queries never return soft-deleted rows.
    /// </summary>
    private static void ApplySoftDeleteQueryFilter(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Equal(
                Expression.Property(parameter, nameof(BaseEntity.IsDeleted)),
                Expression.Constant(false));

            // Named filter so configurations can add further filters without overwriting this one.
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(SoftDeleteFilter, Expression.Lambda(body, parameter));
        }
    }
}
