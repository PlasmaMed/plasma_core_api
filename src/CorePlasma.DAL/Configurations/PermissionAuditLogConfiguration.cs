using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class PermissionAuditLogConfiguration : IEntityTypeConfiguration<PermissionAuditLog>
{
    public void Configure(EntityTypeBuilder<PermissionAuditLog> builder)
    {
        builder.ToTable("PermissionAuditLogs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Action).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ChangedDate).IsRequired();
        builder.Property(x => x.OldValue).HasMaxLength(2000);
        builder.Property(x => x.NewValue).HasMaxLength(2000);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.CorrelationId).HasMaxLength(100);

        builder.HasIndex(x => new { x.EmployeeId, x.ChangedDate });
        builder.HasIndex(x => new { x.PermissionId, x.ChangedDate });
        builder.HasIndex(x => x.ChangedBy);
        builder.HasIndex(x => x.ChangedDate);
    }
}
