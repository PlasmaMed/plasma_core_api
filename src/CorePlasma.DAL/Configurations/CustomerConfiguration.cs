using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class CustomerConfiguration : BaseEntityConfiguration<Customer>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasIndex(x => x.QuanLyId).IsUnique().HasFilter("\"QuanLyId\" IS NOT NULL");
        builder.HasIndex(x => x.NewAppId).IsUnique().HasFilter("\"NewAppId\" IS NOT NULL");

        builder.Property(x => x.CustomerCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CustomerName).HasMaxLength(250).IsRequired();
        builder.Property(x => x.ShortName).HasMaxLength(100);
        builder.Property(x => x.TaxCode).HasMaxLength(50);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.Phone).HasMaxLength(100);
        builder.Property(x => x.Email).HasMaxLength(150);
        builder.Property(x => x.ContactPerson).HasMaxLength(150);
        builder.Property(x => x.ContactPhone).HasMaxLength(100);
        builder.Property(x => x.ContactEmail).HasMaxLength(150);
        builder.Property(x => x.Website).HasMaxLength(250);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.HasIndex(x => x.CustomerCode).IsUnique().HasFilter(NotDeletedFilter);
        builder.HasIndex(x => x.CustomerName);
        builder.HasIndex(x => x.TaxCode);
        builder.HasIndex(x => x.Phone);
        builder.HasIndex(x => x.Email);
        builder.HasIndex(x => x.Status);
    }
}
