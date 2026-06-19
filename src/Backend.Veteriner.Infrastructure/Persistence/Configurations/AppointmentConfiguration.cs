using Backend.Veteriner.Domain.Appointments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        b.ToTable("Appointments");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId)
            .IsRequired();

        b.Property(x => x.ClinicId)
            .IsRequired();

        b.Property(x => x.PetId)
            .IsRequired();

        b.Property(x => x.ScheduledAtUtc)
            .IsRequired();

        b.Property(x => x.DurationMinutes)
            .IsRequired()
            .HasDefaultValue(Appointment.DefaultDurationMinutes);

        b.Property(x => x.AppointmentType)
            .IsRequired()
            .HasConversion<int>();

        b.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>();

        b.Property(x => x.Notes)
            .HasMaxLength(2000);

        b.Property(x => x.MutationSequence)
            .IsRequired()
            .HasDefaultValue(0L)
            .IsConcurrencyToken();

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.ScheduledAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        // Bugünkü durum sayımları (GROUP BY Status) ve klinik + tarih aralığı; Status son kolonda kapsayıcı tarama.
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.ScheduledAtUtc, x.Status });
        b.HasIndex(x => new { x.TenantId, x.PetId });
        // Dashboard clinic-scope: TenantId+ClinicId seek, PetId DISTINCT/GROUP BY, ScheduledAtUtc MAX (covering).
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.PetId })
            .IncludeProperties(x => x.ScheduledAtUtc);
    }
}
