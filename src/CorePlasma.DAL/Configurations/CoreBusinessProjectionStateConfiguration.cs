using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

public sealed class CoreBusinessProjectionStateConfiguration : IEntityTypeConfiguration<CoreBusinessProjectionState>
{
    public void Configure(EntityTypeBuilder<CoreBusinessProjectionState> builder)
    {
        builder.ToTable("CoreBusinessProjectionStates");
        builder.HasKey(x => x.Source);
        builder.Property(x => x.Source).HasMaxLength(16);
        builder.Property(x => x.CatalogHash).HasMaxLength(64).IsRequired();
    }
}
