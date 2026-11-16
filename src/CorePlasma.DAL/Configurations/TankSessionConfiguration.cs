using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class TankSessionConfiguration : BaseEntityConfiguration<TankSession>
{
    protected override bool IndexIsDeleted => false;

    protected override void ConfigureEntity(EntityTypeBuilder<TankSession> builder)
    {
        builder.ToTable("TankSessions");

        builder.Property(x => x.TankRfid).HasMaxLength(50);
        // Plain numeric keeps the source values exactly (no rounding to a fixed scale).
        builder.Property(x => x.PressureBefore).HasColumnType("numeric");
        builder.Property(x => x.PressureAfter).HasColumnType("numeric");
        builder.Property(x => x.WeightBefore).HasColumnType("numeric");
        builder.Property(x => x.WeightAfter).HasColumnType("numeric");

        builder.HasIndex(x => x.SessionId);
        // Lookup by RFID uses an index on upper("TankRfid"), created in the migration (expression index).
        // Retain the insertion-order index for existing queries. Realtime freshness uses the source-sequence
        // expression index created by AddHistoryIngestion, including a UUID fallback for null source IDs.
        builder.HasIndex(x => new { x.TankId, x.Id });
        builder.HasIndex(x => x.LegacyId).IsUnique().HasFilter("\"LegacyId\" IS NOT NULL");

        builder.HasOne(x => x.Session)
            .WithMany(x => x.TankSessions)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Tank)
            .WithMany()
            .HasForeignKey(x => x.TankId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
