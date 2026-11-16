using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

/// <summary>
/// Maps the shared voucher columns; concrete vouchers only configure their own fields.
/// </summary>
public abstract class VoucherBaseConfiguration<T> : BaseEntityConfiguration<T> where T : VoucherBase
{
    protected sealed override void ConfigureEntity(EntityTypeBuilder<T> builder)
    {
        builder.Property(x => x.VoucherNo).HasMaxLength(50).IsRequired();
        builder.Property(x => x.VoucherDate).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.Property(x => x.RejectReason).HasMaxLength(500);
        builder.Property(x => x.CancelReason).HasMaxLength(500);

        builder.HasIndex(x => x.VoucherNo).IsUnique().HasFilter(NotDeletedFilter);
        builder.HasIndex(x => x.VoucherDate);
        builder.HasIndex(x => x.Status);

        ConfigureVoucher(builder);
    }

    protected abstract void ConfigureVoucher(EntityTypeBuilder<T> builder);
}
