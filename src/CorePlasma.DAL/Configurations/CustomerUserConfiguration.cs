using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class CustomerUserConfiguration : BaseEntityConfiguration<CustomerUser>
{
    protected override void ConfigureEntity(EntityTypeBuilder<CustomerUser> builder)
    {
        builder.ToTable("CustomerUsers");

        builder.Property(x => x.Username).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(150);
        builder.Property(x => x.Phone).HasMaxLength(100);
        builder.Property(x => x.Position).HasMaxLength(100);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.AccessFailedCount).HasDefaultValue(0);

        builder.HasIndex(x => x.Username).IsUnique().HasFilter(NotDeletedFilter);
        builder.HasIndex(x => new { x.CustomerId, x.Status });
        builder.HasIndex(x => x.Email);

        builder.HasOne(x => x.Customer)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter("CustomerNotDeleted", x => !x.Customer.IsDeleted);
    }
}
