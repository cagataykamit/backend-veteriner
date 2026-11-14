using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class HospitalizationConfiguration : IEntityTypeConfiguration<Hospitalization>
{
    public void Configure(EntityTypeBuilder<Hospitalization> b)
    {
        b.ToTable("Hospitalizations", t =>
            t.HasCheckConstraint("CK_Hospitalizations_Cancel_Consistent", "([CancelledAtUtc] IS NULL AND [CancelReason] IS NULL AND [CancelledByUserId] IS NULL) OR ([CancelledAtUtc] IS NOT NULL AND [CancelReason] IS NOT NULL AND [CancelledByUserId] IS NOT NULL)"));

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.ExaminationId);
        b.Property(x => x.VisitId);
        b.Property(x => x.AdmittedAtUtc).IsRequired();
        b.Property(x => x.PlannedDischargeAtUtc);
        b.Property(x => x.DischargedAtUtc);

        b.Property(x => x.Reason).IsRequired().HasMaxLength(2000);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.Location).HasMaxLength(200);
        b.Property(x => x.RoomId);
        b.Property(x => x.DischargeNote).HasMaxLength(4000);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.CancelledAtUtc);
        b.Property(x => x.CancelledByUserId);
        b.Property(x => x.CancelReason).HasMaxLength(CorrectionReason.MaxLength);


        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.AdmittedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId });
        b.HasIndex(x => new { x.TenantId, x.RoomId });

        // Oda tanımı silinmez (pasife alınır); yatış kaydı odaya referansını korur.
        b.HasOne<HospitalizationRoom>()
            .WithMany()
            .HasForeignKey(x => x.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        // At most one active (not discharged, not cancelled; Hospitalization.IsActiveExpression) stay per pet per clinic within a tenant.
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.PetId })
            .IsUnique()
            .HasFilter("[DischargedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL");
    }
}
