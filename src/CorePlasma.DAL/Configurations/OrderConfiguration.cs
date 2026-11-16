using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class OrderConfiguration : BaseEntityConfiguration<Order>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.Property(x => x.SourceSystem).HasMaxLength(100);
        builder.Property(x => x.SourceRecordId).HasMaxLength(100);
        builder.Property(x => x.SourceStatusCode).HasMaxLength(100);
        builder.Property(x => x.SourceStatusLabel).HasMaxLength(250);
        builder.HasIndex(x => new { x.SourceSystem, x.SourceRecordId }).IsUnique()
            .HasFilter("\"SourceRecordId\" IS NOT NULL");

        builder.Property(x => x.OrderType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.DeliveryOption).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Store).HasMaxLength(10);
        builder.Property(x => x.Note).HasMaxLength(2000);
        builder.Property(x => x.LegacyId).HasMaxLength(50);

        builder.HasIndex(x => x.OrderNumber);
        builder.HasIndex(x => x.OrderDate);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.CreatedByEmployeeId);
        builder.HasIndex(x => x.DeliveryEmployeeId);
        builder.HasIndex(x => x.LegacyId).IsUnique().HasFilter("\"LegacyId\" IS NOT NULL");

        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CustomerUser).WithMany().HasForeignKey(x => x.CustomerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByEmployee).WithMany().HasForeignKey(x => x.CreatedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DeliveryEmployee).WithMany().HasForeignKey(x => x.DeliveryEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class OrderTankConfiguration : BaseEntityConfiguration<OrderTank>
{
    protected override bool IndexIsDeleted => false;

    protected override void ConfigureEntity(EntityTypeBuilder<OrderTank> builder)
    {
        builder.ToTable("OrderTanks");

        builder.Property(x => x.TankLegacyId).HasMaxLength(50);

        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.TankId);

        builder.HasOne(x => x.Order).WithMany(x => x.Tanks).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Tank).WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Restrict);
    }
}
