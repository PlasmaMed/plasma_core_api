using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class PermissionConfiguration : BaseEntityConfiguration<Permission>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");

        builder.Property(x => x.PermissionCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PermissionName).HasMaxLength(250).IsRequired();
        builder.Property(x => x.ModuleCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.FunctionCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ActionCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.PermissionCode).IsUnique().HasFilter(NotDeletedFilter);
        builder.HasIndex(x => new { x.ModuleCode, x.FunctionCode, x.ActionCode });
        builder.HasIndex(x => x.SortOrder);
    }
}
