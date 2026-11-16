using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class TankTransferConfiguration : VoucherBaseConfiguration<TankTransfer>
{
    protected override void ConfigureVoucher(EntityTypeBuilder<TankTransfer> builder)
    {
        builder.ToTable("TankTransfers");
        builder.Property(x => x.SourceSystem).HasMaxLength(100);
        builder.Property(x => x.SourceRecordId).HasMaxLength(100);
        builder.Property(x => x.SourceStatusCode).HasMaxLength(100);
        builder.Property(x => x.SourceStatusLabel).HasMaxLength(250);
        // Source-owned transfers preserve complete notes. Removing the shared
        // voucher width maps to nvarchar(max) on SQL Server and text on PostgreSQL.
        builder.Property(x => x.Note).Metadata.SetMaxLength(null);
        builder.HasIndex(x => new { x.SourceSystem, x.SourceRecordId }).IsUnique()
            .HasFilter("\"SourceRecordId\" IS NOT NULL");

        builder.Property(x => x.ToLocation).HasMaxLength(250);
        builder.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30);

        builder.HasOne(x => x.ToCustomer)
            .WithMany()
            .HasForeignKey(x => x.ToCustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ToEmployee)
            .WithMany()
            .HasForeignKey(x => x.ToEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class TankTransferItemConfiguration : IEntityTypeConfiguration<TankTransferItem>
{
    public void Configure(EntityTypeBuilder<TankTransferItem> builder)
    {
        builder.ToTable("TankTransferItems");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TankCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.FromLocation).HasMaxLength(250);
        builder.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasIndex(x => new { x.TankTransferId, x.TankId }).IsUnique();
        builder.HasIndex(x => x.TankId);

        // Items belong to the voucher. Vouchers are only soft-deleted, so this never removes history.
        builder.HasOne(x => x.TankTransfer)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.TankTransferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Tank)
            .WithMany()
            .HasForeignKey(x => x.TankId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.TankTransfer.IsDeleted);
    }
}
