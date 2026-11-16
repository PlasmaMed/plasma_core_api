using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class DataChangeLogConfiguration : IEntityTypeConfiguration<DataChangeLog>
{
    public void Configure(EntityTypeBuilder<DataChangeLog> builder)
    {
        builder.ToTable("DataChangeLogs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EntityName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Changes).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ChangedByUsername).HasMaxLength(100);
        builder.Property(x => x.ChangedByUserType).HasMaxLength(20);
        builder.Property(x => x.ChangedDate).IsRequired();
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.CorrelationId).HasMaxLength(100);

        builder.HasIndex(x => new { x.EntityName, x.EntityId, x.ChangedDate });
        builder.HasIndex(x => x.ChangedBy);
        builder.HasIndex(x => x.ChangedDate);
    }
}
