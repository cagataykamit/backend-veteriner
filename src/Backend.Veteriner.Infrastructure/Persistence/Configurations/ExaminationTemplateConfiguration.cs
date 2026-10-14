using Backend.Veteriner.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ExaminationTemplateConfiguration : IEntityTypeConfiguration<ExaminationTemplate>
{
    public const string NameIndexName = "IX_ExaminationTemplates_TenantId_NormalizedName";

    public void Configure(EntityTypeBuilder<ExaminationTemplate> b)
    {
        b.ToTable("ExaminationTemplates");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId);
        b.Property(x => x.Name).IsRequired().HasMaxLength(ExaminationTemplate.MaxNameLength);
        b.Property(x => x.NormalizedName).IsRequired().HasMaxLength(ExaminationTemplate.MaxNameLength);
        b.Property(x => x.VisitReason).HasMaxLength(ExaminationTemplate.MaxVisitReasonLength);
        b.Property(x => x.Findings).HasMaxLength(ExaminationTemplate.MaxTextLength);
        b.Property(x => x.Plan).HasMaxLength(ExaminationTemplate.MaxTextLength);
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.Ignore(x => x.IsSystem);

        b.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(l => l.ExaminationTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Kiracı içinde Türkçe anahtar tek (sistem satırları seed'de idempotent eklenir).
        b.HasIndex(x => new { x.TenantId, x.NormalizedName })
            .IsUnique()
            .HasDatabaseName(NameIndexName);
        b.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}
