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

        b.Property(x => x.AppointmentType)
            .IsRequired()
            .HasConversion<int>();

        b.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>();

        b.Property(x => x.Notes)
            .HasMaxLength(2000);

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.ScheduledAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.PetId });
    }
}
