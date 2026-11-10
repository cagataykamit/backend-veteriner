using Backend.Veteriner.Domain.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>Eski program klinik geçmiş kaydı (ADR-048 T1). Hasta ekranı ve zaman çizelgesi <c>(TenantId, PetId, RecordedOn)</c> okur.</summary>
public sealed class ImportedClinicalRecordConfiguration : IEntityTypeConfiguration<ImportedClinicalRecord>
{
    public void Configure(EntityTypeBuilder<ImportedClinicalRecord> b)
    {
        b.ToTable("ImportedClinicalRecords");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.ClientId).IsRequired();
        b.Property(x => x.ImportBatchId).IsRequired();
        b.Property(x => x.RecordedOn).IsRequired();
        b.Property(x => x.RecordType).HasMaxLength(ImportedClinicalRecord.MaxRecordTypeLength);
        b.Property(x => x.Complaint).HasMaxLength(ImportedClinicalRecord.MaxComplaintLength);
        b.Property(x => x.History).HasMaxLength(ImportedClinicalRecord.MaxHistoryLength);
        b.Property(x => x.Findings).HasMaxLength(ImportedClinicalRecord.MaxFindingsLength);
        b.Property(x => x.Diagnosis).HasMaxLength(ImportedClinicalRecord.MaxDiagnosisLength);
        b.Property(x => x.Treatment).HasMaxLength(ImportedClinicalRecord.MaxTreatmentLength);
        b.Property(x => x.Prescription).HasMaxLength(ImportedClinicalRecord.MaxPrescriptionLength);
        b.Property(x => x.LabResults).HasMaxLength(ImportedClinicalRecord.MaxLabResultsLength);
        b.Property(x => x.Notes).HasMaxLength(ImportedClinicalRecord.MaxNotesLength);
        b.Property(x => x.VeterinarianName).HasMaxLength(ImportedClinicalRecord.MaxVeterinarianNameLength);
        b.Property(x => x.WeightKg).HasPrecision(6, 2);
        b.Property(x => x.TemperatureC).HasPrecision(4, 1);
        b.Property(x => x.CreatedAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.PetId, x.RecordedOn });
        b.HasIndex(x => new { x.TenantId, x.ClientId });
        b.HasIndex(x => x.ImportBatchId);

        b.HasOne<ImportBatch>()
            .WithMany()
            .HasForeignKey(x => x.ImportBatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Eski program hesap hareketi (ADR-048 T4). Müşteri hesap ekranı <c>(TenantId, ClientId, OccurredOn)</c> okur.</summary>
public sealed class ImportedAccountEntryConfiguration : IEntityTypeConfiguration<ImportedAccountEntry>
{
    public void Configure(EntityTypeBuilder<ImportedAccountEntry> b)
    {
        b.ToTable("ImportedAccountEntries");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ClientId).IsRequired();
        b.Property(x => x.ImportBatchId).IsRequired();
        b.Property(x => x.OccurredOn).IsRequired();
        b.Property(x => x.Kind).IsRequired();
        b.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        b.Property(x => x.Description).HasMaxLength(ImportedAccountEntry.MaxDescriptionLength);
        b.Property(x => x.PaymentMethod).HasMaxLength(ImportedAccountEntry.MaxPaymentMethodLength);
        b.Property(x => x.DocumentNumber).HasMaxLength(ImportedAccountEntry.MaxDocumentNumberLength);
        b.Property(x => x.PetName).HasMaxLength(ImportedAccountEntry.MaxPetNameLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ClientId, x.OccurredOn });
        b.HasIndex(x => x.ImportBatchId);

        b.HasOne<ImportBatch>()
            .WithMany()
            .HasForeignKey(x => x.ImportBatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
