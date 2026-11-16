using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class EmployeeUserConfiguration : BaseEntityConfiguration<EmployeeUser>
{
    protected override void ConfigureEntity(EntityTypeBuilder<EmployeeUser> builder)
    {
        builder.ToTable("EmployeeUsers");

        builder.Property(x => x.Username).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.IsSuperAdmin).HasDefaultValue(false);
        builder.Property(x => x.AccessFailedCount).HasDefaultValue(0);

        builder.HasIndex(x => x.Username).IsUnique().HasFilter(NotDeletedFilter);
        // One active account per employee.
        builder.HasIndex(x => x.EmployeeId).IsUnique().HasFilter(NotDeletedFilter);
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.Employee)
            .WithOne(x => x.User)
            .HasForeignKey<EmployeeUser>(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter("EmployeeNotDeleted", x => !x.Employee.IsDeleted);
    }
}
