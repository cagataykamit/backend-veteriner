using Backend.Veteriner.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ClinicalSnippetConfiguration : IEntityTypeConfiguration<ClinicalSnippet>
{
    public void Configure(EntityTypeBuilder<ClinicalSnippet> b)
    {
        b.ToTable("ClinicalSnippets");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.Shortcut).IsRequired().HasMaxLength(ClinicalSnippet.MaxShortcutLength);
        b.Property(x => x.Title).IsRequired().HasMaxLength(ClinicalSnippet.MaxTitleLength);
        b.Property(x => x.NormalizedTitle).IsRequired().HasMaxLength(ClinicalSnippet.MaxTitleLength);
        b.Property(x => x.Body).IsRequired().HasMaxLength(ClinicalSnippet.MaxBodyLength);
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // Kiracı içinde kısayol tek (pasif dahil); seçici #kısayol ile bu indeks üzerinden süzer.
        b.HasIndex(x => new { x.TenantId, x.Shortcut })
            .IsUnique()
            .HasDatabaseName("IX_ClinicalSnippets_TenantId_Shortcut");
        b.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}
