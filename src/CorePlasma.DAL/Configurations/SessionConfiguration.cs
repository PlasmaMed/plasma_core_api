using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class SessionConfiguration : BaseEntityConfiguration<Session>
{
    protected override bool IndexIsDeleted => false;

    protected override void ConfigureEntity(EntityTypeBuilder<Session> builder)
    {
        // No EndTime >= StartTime check: imported history keeps the source values as they are.
        builder.ToTable("Sessions");

        builder.Property(x => x.StartTime).IsRequired();
        builder.Property(x => x.SourceRevision).HasDefaultValue(0L);
        builder.Property(x => x.Mode).HasMaxLength(100);
        builder.Property(x => x.AverageSpeed).HasMaxLength(50);
        builder.Property(x => x.PlasmaOnTime).HasMaxLength(50);
        builder.Property(x => x.EnvironmentTemperatureBefore).HasMaxLength(50);
        builder.Property(x => x.EnvironmentTemperatureAfter).HasMaxLength(50);
        builder.Property(x => x.HandleTemperatureBefore).HasMaxLength(50);
        builder.Property(x => x.HandleTemperatureAfter).HasMaxLength(50);
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.Property(x => x.UserCode).HasMaxLength(200);

        builder.HasIndex(x => new { x.EngineId, x.StartTime });
        builder.HasIndex(x => x.StartTime);
        builder.HasIndex(x => x.LegacyId).IsUnique().HasFilter("\"LegacyId\" IS NOT NULL");

        builder.HasOne(x => x.Engine)
            .WithMany(x => x.Sessions)
            .HasForeignKey(x => x.EngineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
