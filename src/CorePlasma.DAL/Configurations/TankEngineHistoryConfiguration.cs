using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class TankEngineHistoryConfiguration : BaseEntityConfiguration<TankEngineHistory>
{
    protected override bool IndexIsDeleted => false;

    protected override void ConfigureEntity(EntityTypeBuilder<TankEngineHistory> builder)
    {
        builder.ToTable("TankEngineHistories");

        // Some source rows hold a raw device frame (hex, up to 272 chars) instead of a tag code; kept as is.
        builder.Property(x => x.TankRfid).HasMaxLength(300);
        builder.Property(x => x.EventTime).IsRequired();

        builder.HasIndex(x => new { x.EngineId, x.EventTime });
        // Lookup by RFID uses an index on upper("TankRfid"), created in the migration (expression index).
        builder.HasIndex(x => x.TankId);
        builder.HasIndex(x => x.LegacyId).IsUnique().HasFilter("\"LegacyId\" IS NOT NULL");

        builder.HasOne(x => x.Engine)
            .WithMany()
            .HasForeignKey(x => x.EngineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Tank)
            .WithMany()
            .HasForeignKey(x => x.TankId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
