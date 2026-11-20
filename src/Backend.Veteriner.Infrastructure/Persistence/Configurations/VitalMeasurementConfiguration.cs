using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.VitalMeasurements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class VitalMeasurementConfiguration : IEntityTypeConfiguration<VitalMeasurement>
{
    public void Configure(EntityTypeBuilder<VitalMeasurement> b)
    {
        b.ToTable("VitalMeasurements", t =>
        {
            t.HasCheckConstraint("CK_VitalMeasurements_Cancel_Consistent", "([CancelledAtUtc] IS NULL AND [CancelReason] IS NULL AND [CancelledByUserId] IS NULL) OR ([CancelledAtUtc] IS NOT NULL AND [CancelReason] IS NOT NULL AND [CancelledByUserId] IS NOT NULL)");
            t.HasCheckConstraint("CK_VitalMeasurements_LateEntry_Consistent", "([AddedAfterFinalization] = 0 AND [LateEntryReason] IS NULL) OR ([AddedAfterFinalization] = 1 AND [LateEntryReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_VitalMeasurements_SingleLink", "[HospitalizationId] IS NULL OR [ExaminationId] IS NULL");
            t.HasCheckConstraint("CK_VitalMeasurements_HasValue", "[TemperatureC] IS NOT NULL OR [HeartRateBpm] IS NOT NULL OR [RespiratoryRatePerMin] IS NOT NULL OR [WeightKg] IS NOT NULL");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.HospitalizationId);
        b.Property(x => x.ExaminationId);
        b.Property(x => x.MeasuredAtUtc).IsRequired();

        b.Property(x => x.TemperatureC).HasPrecision(4, VitalSigns.TemperatureDecimals);
        b.Property(x => x.HeartRateBpm);
        b.Property(x => x.RespiratoryRatePerMin);
        b.Property(x => x.WeightKg).HasPrecision(6, VitalSigns.WeightDecimals);
        b.Property(x => x.Note).HasMaxLength(VitalMeasurement.MaxNoteLength);

        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.CancelledAtUtc);
        b.Property(x => x.CancelledByUserId);
        b.Property(x => x.CancelReason).HasMaxLength(CorrectionReason.MaxLength);
        b.Property(x => x.AddedAfterFinalization).IsRequired();
        b.Property(x => x.LateEntryReason).HasMaxLength(CorrectionReason.MaxLength);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.HasIndex(x => new { x.TenantId, x.PetId, x.MeasuredAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.HospitalizationId });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId });
    }
}
