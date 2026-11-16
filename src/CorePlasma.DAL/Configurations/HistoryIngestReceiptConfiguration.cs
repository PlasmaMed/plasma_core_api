using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public sealed class HistoryIngestReceiptConfiguration : IEntityTypeConfiguration<HistoryIngestReceipt>
{
    public void Configure(EntityTypeBuilder<HistoryIngestReceipt> builder)
    {
        builder.ToTable("HistoryIngestReceipts");
        builder.HasKey(x => x.EventId);
        builder.Property(x => x.EventId).ValueGeneratedNever();
        builder.Property(x => x.Source).HasMaxLength(20).IsRequired();
        builder.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Disposition).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.Source, x.SourceRevision }).IsUnique();
        builder.HasIndex(x => x.SessionId);
        builder.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
