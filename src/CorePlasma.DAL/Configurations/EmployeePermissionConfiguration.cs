using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class EmployeePermissionConfiguration : IEntityTypeConfiguration<EmployeePermission>
{
    public void Configure(EntityTypeBuilder<EmployeePermission> builder)
    {
        builder.ToTable("EmployeePermissions");

        // Composite PK also prevents granting the same permission twice.
        builder.HasKey(x => new { x.EmployeeId, x.PermissionId });

        builder.Property(x => x.GrantedDate).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        // Reverse lookup: which employees hold a permission.
        builder.HasIndex(x => new { x.PermissionId, x.EmployeeId });

        builder.HasOne(x => x.Employee)
            .WithMany(x => x.EmployeePermissions)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Permission)
            .WithMany(x => x.EmployeePermissions)
            .HasForeignKey(x => x.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.Employee.IsDeleted && !x.Permission.IsDeleted);
    }
}
