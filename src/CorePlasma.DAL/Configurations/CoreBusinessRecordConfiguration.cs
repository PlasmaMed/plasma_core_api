using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public sealed class CoreBusinessRecordConfiguration : BaseEntityConfiguration<CoreBusinessRecord>
{
    protected override void ConfigureEntity(EntityTypeBuilder<CoreBusinessRecord> builder)
    {
        builder.ToTable("CoreBusinessRecords");
        builder.Property(x => x.Source).HasMaxLength(16).IsRequired();
        builder.Property(x => x.SourceTable).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Domain).HasMaxLength(40).IsRequired();
        builder.Property(x => x.SourceKeyHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(500).IsRequired();
        builder.Property(x => x.SourceStatus).HasMaxLength(100);
        builder.Property(x => x.LastRawEventId).HasMaxLength(20);
        builder.Property(x => x.SnapshotHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExternalKeyJson).IsRequired();
        builder.Property(x => x.SnapshotJson).IsRequired();
        builder.HasIndex(x => new { x.Source, x.SourceTable, x.SourceKeyHash }).IsUnique();
        builder.HasIndex(x => new { x.Source, x.SourceTable, x.IsDeleted });
    }
}
