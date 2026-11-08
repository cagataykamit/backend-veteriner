using Backend.Veteriner.Domain.Clinics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ClinicAppointmentSettingsConfiguration : IEntityTypeConfiguration<ClinicAppointmentSettings>
{
    public void Configure(EntityTypeBuilder<ClinicAppointmentSettings> b)
    {
        b.ToTable("ClinicAppointmentSettings");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.DefaultAppointmentDurationMinutes).IsRequired();
        b.Property(x => x.SlotIntervalMinutes).IsRequired();
        b.Property(x => x.AllowOverlappingAppointments).IsRequired();
        // Varsayılan açık (TIER-019) domain'dedir; mevcut satırlar migration'da açık doldurulur. Veritabanı varsayılanı
        // tanımlanmaz: "false" değeri EF sentinel'i sayılıp yok sayılmasın.
        b.Property(x => x.OwnerCancelLinkEnabled).IsRequired();
        // Bekleme listesi otomatik teklifi (TIER-018): aynı kural, mevcut satırlar migration'da açık doldurulur.
        b.Property(x => x.WaitlistAutoOfferEnabled).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.TenantId, x.ClinicId }).IsUnique();

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
