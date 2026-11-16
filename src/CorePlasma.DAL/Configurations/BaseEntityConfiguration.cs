using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CorePlasma.DAL.Configurations;

/// <summary>
/// Shared mapping for audit/soft-delete columns. The soft-delete query filter is applied in ApplicationDbContext.
/// </summary>
public abstract class BaseEntityConfiguration<T> : IEntityTypeConfiguration<T> where T : BaseEntity
{
    public const string NotDeletedFilter = "\"IsDeleted\" = false";

    public void Configure(EntityTypeBuilder<T> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CreatedDate).IsRequired();
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        if (IndexIsDeleted)
        {
            builder.HasIndex(x => x.IsDeleted);
        }

        ConfigureEntity(builder);
    }

    /// <summary>
    /// Event tables with millions of rows are almost never soft-deleted, so an index on IsDeleted only costs space.
    /// </summary>
    protected virtual bool IndexIsDeleted => true;

    protected abstract void ConfigureEntity(EntityTypeBuilder<T> builder);
}
