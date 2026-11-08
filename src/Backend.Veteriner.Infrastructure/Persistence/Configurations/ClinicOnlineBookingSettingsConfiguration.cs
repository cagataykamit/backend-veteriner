using Backend.Veteriner.Domain.Clinics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ClinicOnlineBookingSettingsConfiguration : IEntityTypeConfiguration<ClinicOnlineBookingSettings>
{
    public void Configure(EntityTypeBuilder<ClinicOnlineBookingSettings> b)
    {
        b.ToTable("ClinicOnlineBookingSettings");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.IsEnabled).IsRequired();
        b.Property(x => x.MinLeadMinutes).IsRequired();
        b.Property(x => x.MaxAdvanceDays).IsRequired();
        // Varsayılan açık (TIER-017); mevcut satırlar migration'da açık başlar. Model varsayılanı yok: EF yanlış değerini yazsın.
        b.Property(x => x.ShowNextAvailableSlots).IsRequired();
        b.Ignore(x => x.PublishesNextAvailableSlots);
        b.Property(x => x.BookingCode)
            .IsRequired()
            .HasMaxLength(ClinicOnlineBookingSettings.BookingCodeLength)
            .IsUnicode(false);
        b.Property(x => x.BookingCodeGeneratedAtUtc).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.TenantId, x.ClinicId }).IsUnique();

        // Kimliksiz bağlantı ucu kodu kiracılar arası çözer (ADR-042 K1).
        b.HasIndex(x => x.BookingCode).IsUnique();

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
