using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Waitlist;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>Hatırlatma "onayla / iptal et", "takvime ekle" ve bekleme listesi teklifi bağlantısı (TIER-019/020/018); ham token yalnız e-postada, burada SHA-256 özeti.</summary>
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
        b.Property(x => x.Purpose).HasConversion<int>().IsRequired();
        b.Property(x => x.ExpiresAtUtc).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.ClosedAtUtc);
        b.Property(x => x.UsedAction).HasConversion<int?>();
        b.Property(x => x.WaitlistOfferId);

        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.AppointmentId });
        b.HasIndex(x => x.WaitlistOfferId).HasFilter("[WaitlistOfferId] IS NOT NULL");

        // Bekleme listesi teklifi (TIER-018): teklif silinmez; bağlantı teklifle birlikte kalır.
        b.HasOne<WaitlistOffer>()
            .WithMany()
            .HasForeignKey(x => x.WaitlistOfferId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
