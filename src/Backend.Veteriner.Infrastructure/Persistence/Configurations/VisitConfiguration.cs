using Backend.Veteriner.Domain.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    /// <summary>Hayvan başına tek aktif (tamamlanmamış, yanlış geliş olmayan) geliş.</summary>
    public const string ActiveVisitPerPetIndexName = "UX_Visits_TenantId_PetId_Active";

    /// <summary>Randevu başına tek (yanlış geliş olmayan) geliş.</summary>
    public const string VisitPerAppointmentIndexName = "UX_Visits_TenantId_AppointmentId";

    public void Configure(EntityTypeBuilder<Visit> b)
    {
        b.ToTable("Visits");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.AppointmentId);
        b.Property(x => x.ResponsibleVeterinarianUserId);
        b.Property(x => x.ArrivedAtUtc).IsRequired();

        b.Property(x => x.CareStatus)
            .IsRequired()
            .HasConversion<int>();

        b.Property(x => x.StartedAtUtc);
        b.Property(x => x.CompletedAtUtc);
        b.Property(x => x.VoidedAtUtc);
        b.Property(x => x.VoidReason).HasMaxLength(Visit.MaxCorrectionReasonLength);
        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();

        b.Property(x => x.MutationSequence)
            .IsRequired()
            .HasDefaultValue(0L)
            .IsConcurrencyToken();

        b.Ignore(x => x.IsVoided);

        // CareStatus.Completed = 2
        b.HasIndex(x => new { x.TenantId, x.PetId })
            .IsUnique()
            .HasFilter("[CareStatus] <> 2 AND [VoidedAtUtc] IS NULL")
            .HasDatabaseName(ActiveVisitPerPetIndexName);

        b.HasIndex(x => new { x.TenantId, x.AppointmentId })
            .IsUnique()
            .HasFilter("[AppointmentId] IS NOT NULL AND [VoidedAtUtc] IS NULL")
            .HasDatabaseName(VisitPerAppointmentIndexName);

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.ArrivedAtUtc });
    }
}
