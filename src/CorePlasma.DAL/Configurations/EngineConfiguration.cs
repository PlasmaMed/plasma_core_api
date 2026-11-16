using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class EngineConfiguration : BaseEntityConfiguration<Engine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Engine> builder)
    {
        builder.ToTable("Engines");
        builder.HasIndex(x => x.QuanLyId).IsUnique().HasFilter("\"QuanLyId\" IS NOT NULL");
        builder.HasIndex(x => x.NewAppId).IsUnique().HasFilter("\"NewAppId\" IS NOT NULL");

        builder.Property(x => x.EngineCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.EngineName).HasMaxLength(250).IsRequired();
        builder.Property(x => x.SerialNumber).HasMaxLength(100);
        builder.Property(x => x.Model).HasMaxLength(100);
        builder.Property(x => x.Manufacturer).HasMaxLength(150);
        builder.Property(x => x.Power).HasPrecision(18, 2);
        builder.Property(x => x.Voltage).HasPrecision(18, 2);
        builder.Property(x => x.Current).HasPrecision(18, 2);
        builder.Property(x => x.CuttingThickness).HasPrecision(10, 2);
        builder.Property(x => x.CurrentLocation).HasMaxLength(250);
        builder.Property(x => x.Department).HasMaxLength(100);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.EngineType).HasMaxLength(30);
        builder.Property(x => x.TankType).HasMaxLength(20);
        builder.Property(x => x.Store).HasMaxLength(10);
        builder.Property(x => x.LegacyId).HasMaxLength(50);

        builder.HasIndex(x => x.EngineCode).IsUnique().HasFilter(NotDeletedFilter);
        builder.HasIndex(x => x.EngineName);
        builder.HasIndex(x => x.SerialNumber);
        builder.HasIndex(x => x.Department);
        builder.HasIndex(x => x.CurrentLocation);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.NextMaintenanceDate);
        builder.HasIndex(x => x.EngineType);
        builder.HasIndex(x => x.LegacyId).IsUnique().HasFilter("\"LegacyId\" IS NOT NULL AND " + NotDeletedFilter);
        builder.HasIndex(x => x.Store);

        builder.HasOne(x => x.ResponsibleEmployee)
            .WithMany(x => x.ResponsibleEngines)
            .HasForeignKey(x => x.ResponsibleEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
