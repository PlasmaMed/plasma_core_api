using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class EmployeeConfiguration : BaseEntityConfiguration<Employee>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");
        builder.HasIndex(x => x.QuanLyId).IsUnique().HasFilter("\"QuanLyId\" IS NOT NULL");
        builder.HasIndex(x => x.NewAppId).IsUnique().HasFilter("\"NewAppId\" IS NOT NULL");

        builder.Property(x => x.EmployeeCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Gender).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(100);
        builder.Property(x => x.Email).HasMaxLength(150);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.Department).HasMaxLength(100);
        builder.Property(x => x.Position).HasMaxLength(100);
        builder.Property(x => x.IdentityNumber).HasMaxLength(50);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);

        builder.HasIndex(x => x.EmployeeCode).IsUnique().HasFilter(NotDeletedFilter);
        builder.HasIndex(x => x.FullName);
        builder.HasIndex(x => x.Email);
        builder.HasIndex(x => x.Phone);
        builder.HasIndex(x => x.IdentityNumber);
        builder.HasIndex(x => x.Department);
        builder.HasIndex(x => x.Position);
        builder.HasIndex(x => x.Status);
    }
}
