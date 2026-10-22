using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ImagingRecordConfiguration : IEntityTypeConfiguration<ImagingRecord>
{
    public void Configure(EntityTypeBuilder<ImagingRecord> b)
    {
        b.ToTable("ImagingRecords");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.ExaminationId);
        b.Property(x => x.PerformedAtUtc).IsRequired();
        b.Property(x => x.Modality).IsRequired();
        b.Property(x => x.Description).HasMaxLength(ImagingRecord.DescriptionMaxLength);
        b.Property(x => x.ClinicalNote).HasMaxLength(ImagingRecord.ClinicalNoteMaxLength);
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.AddedAfterExaminationFinalization).IsRequired();
        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.CancelledAtUtc);
        b.Property(x => x.CancelledByUserId);
        b.Property(x => x.CancelReason).HasMaxLength(CorrectionReason.MaxLength);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.PerformedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId });
    }
}
