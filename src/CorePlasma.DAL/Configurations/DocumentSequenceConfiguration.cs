using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public class DocumentSequenceConfiguration : BaseEntityConfiguration<DocumentSequence>
{
    protected override void ConfigureEntity(EntityTypeBuilder<DocumentSequence> builder)
    {
        builder.ToTable("DocumentSequences");

        builder.Property(x => x.SequenceCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SequenceName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Prefix).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Separator).HasMaxLength(5).IsRequired();
        builder.Property(x => x.DateFormat).HasMaxLength(20);
        builder.Property(x => x.ResetPolicy).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CurrentPeriod).HasMaxLength(20).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.SequenceCode).IsUnique().HasFilter(NotDeletedFilter);
    }
}
