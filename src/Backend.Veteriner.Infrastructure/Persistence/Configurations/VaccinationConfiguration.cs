using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Vaccinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class VaccinationConfiguration : IEntityTypeConfiguration<Vaccination>
{
    public void Configure(EntityTypeBuilder<Vaccination> b)
    {
        b.ToTable("Vaccinations");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ExaminationId);
        b.Property(x => x.VaccineName).IsRequired().HasMaxLength(300);
        b.Property(x => x.VaccineDefinitionId);
        b.Property(x => x.AppliedAtUtc);
        b.Property(x => x.DueAtUtc);
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        // HBS ATS geri kaydı (ADR-036 Dilim 2); mevcut kayıtlar Girilmedi (0) başlar.
        b.Property(x => x.HbsStatus).IsRequired();
        b.Property(x => x.HbsReferenceNumber).HasMaxLength(HbsEntryRules.ReferenceNumberMaxLength);
        b.Property(x => x.HbsNotRequiredReason).HasMaxLength(CorrectionReason.MaxLength);
        b.Property(x => x.HbsMarkedAtUtc);
        b.Property(x => x.HbsMarkedByUserId);
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.HbsStatus });

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.DueAtUtc });
        b.HasIndex(x => new { x.TenantId, x.AppliedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId });
        b.HasIndex(x => x.VaccineDefinitionId);

        // İçe aktarılan kayıtlar: hatırlatma dışlaması ve parti geri alması bu indeksten okur.
        b.Property(x => x.ImportBatchId);
        b.HasIndex(x => x.ImportBatchId).HasFilter("[ImportBatchId] IS NOT NULL");
        b.HasOne<ImportBatch>()
            .WithMany()
            .HasForeignKey(x => x.ImportBatchId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        b.HasOne<VaccineDefinition>()
            .WithMany()
            .HasForeignKey(x => x.VaccineDefinitionId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
