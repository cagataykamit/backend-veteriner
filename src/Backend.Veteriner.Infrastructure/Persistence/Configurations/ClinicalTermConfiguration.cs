using Backend.Veteriner.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ClinicalTermConfiguration : IEntityTypeConfiguration<ClinicalTerm>
{
    public void Configure(EntityTypeBuilder<ClinicalTerm> b)
    {
        b.ToTable("ClinicalTerms");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId);
        b.Property(x => x.Kind).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(ClinicalTerm.MaxNameLength);
        b.Property(x => x.NormalizedName).IsRequired().HasMaxLength(ClinicalTerm.MaxNameLength);
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.Ignore(x => x.IsSystem);

        // Kiracı içinde aynı tür + Türkçe anahtar tek (sistem satırları seed'de idempotent eklenir).
        b.HasIndex(x => new { x.TenantId, x.Kind, x.NormalizedName })
            .IsUnique()
            .HasDatabaseName("IX_ClinicalTerms_TenantId_Kind_NormalizedName");
        b.HasIndex(x => new { x.TenantId, x.Kind, x.IsActive });
    }
}
