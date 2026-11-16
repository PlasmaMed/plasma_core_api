using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public sealed class LegacySyncMappingConfiguration : IEntityTypeConfiguration<LegacySyncMapping>
{
    public void Configure(EntityTypeBuilder<LegacySyncMapping> builder)
    {
        builder.ToTable("LegacySyncMappings");
        builder.HasKey(x => new { x.Source, x.Entity, x.EntityId });
        builder.Property(x => x.Source).HasMaxLength(40).IsRequired();
        builder.Property(x => x.SourceSystem).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Entity).HasMaxLength(16).IsRequired();
        builder.Property(x => x.SnapshotJson).HasColumnType("jsonb").IsRequired();
        // Customer aliases remain many-to-one; assets/users may be explicitly bound once per namespace.
        builder.HasIndex(x => new { x.Source, x.Entity, x.CoreId }).IsUnique()
            .HasFilter("\"Entity\" IN ('tank','engine','user') AND \"CoreId\" IS NOT NULL");
        builder.HasIndex(x => new { x.Entity, x.CoreId });
        // Internal bridge allocates from MAX(LastSequence) under the ingestion lock.
        builder.HasIndex(x => new { x.Source, x.LastSequence });
    }
}

public sealed class LegacySyncReceiptConfiguration : IEntityTypeConfiguration<LegacySyncReceipt>
{
    public void Configure(EntityTypeBuilder<LegacySyncReceipt> builder)
    {
        builder.ToTable("LegacySyncReceipts");
        builder.HasKey(x => x.EventId);
        builder.Property(x => x.EventId).ValueGeneratedNever();
        builder.Property(x => x.Source).HasMaxLength(40).IsRequired();
        builder.Property(x => x.SourceSystem).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Entity).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Operation).HasMaxLength(16).IsRequired();
        builder.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.Source, x.Sequence }).IsUnique();
    }
}
