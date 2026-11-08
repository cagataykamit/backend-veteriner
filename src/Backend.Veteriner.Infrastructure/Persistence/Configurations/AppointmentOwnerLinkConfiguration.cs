using Backend.Veteriner.Domain.Appointments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>Hatırlatma "onayla / iptal et" bağlantısı (TIER-019); ham token yalnız e-postada, burada SHA-256 özeti.</summary>
public sealed class AppointmentOwnerLinkConfiguration : IEntityTypeConfiguration<AppointmentOwnerLink>
{
    public void Configure(EntityTypeBuilder<AppointmentOwnerLink> b)
    {
        b.ToTable("AppointmentOwnerLinks");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.AppointmentId).IsRequired();
        b.Property(x => x.TokenHash).IsRequired().HasMaxLength(AppointmentOwnerLink.TokenHashLength);
        b.Property(x => x.ExpiresAtUtc).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.ClosedAtUtc);
        b.Property(x => x.UsedAction).HasConversion<int?>();

        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.AppointmentId });

        b.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
