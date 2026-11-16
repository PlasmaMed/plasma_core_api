using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class TankConfiguration : BaseEntityConfiguration<Tank>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Tank> builder)
    {
        builder.ToTable("Tanks");
        builder.HasIndex(x => x.QuanLyId).IsUnique().HasFilter("\"QuanLyId\" IS NOT NULL");
        builder.HasIndex(x => x.NewAppId).IsUnique().HasFilter("\"NewAppId\" IS NOT NULL");

        builder.Property(x => x.TankCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SerialNumber).HasMaxLength(100);
        builder.Property(x => x.TankType).HasMaxLength(100);
        builder.Property(x => x.GasType).HasMaxLength(50);
        builder.Property(x => x.Manufacturer).HasMaxLength(150);
        builder.Property(x => x.Capacity).HasPrecision(18, 3);
        builder.Property(x => x.WorkingPressure).HasPrecision(18, 3);
        builder.Property(x => x.CurrentPressure).HasPrecision(18, 3);
        builder.Property(x => x.Unit).HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.CurrentLocation).HasMaxLength(250);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Store).HasMaxLength(10);
        builder.Property(x => x.LegacyId).HasMaxLength(50);
        builder.Property(x => x.LegacyInvoiceId).HasMaxLength(50);

        builder.HasIndex(x => x.TankCode).IsUnique().HasFilter(NotDeletedFilter);
        builder.HasIndex(x => x.SerialNumber);
        builder.HasIndex(x => x.GasType);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CurrentLocation);
        builder.HasIndex(x => x.NextInspectionDate);
        builder.HasIndex(x => x.TankType);
        builder.HasIndex(x => x.LegacyId).IsUnique().HasFilter("\"LegacyId\" IS NOT NULL AND " + NotDeletedFilter);
        builder.HasIndex(x => x.Store);

        builder.HasOne(x => x.Customer)
            .WithMany(x => x.Tanks)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AssignedEmployee)
            .WithMany(x => x.AssignedTanks)
            .HasForeignKey(x => x.AssignedEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
