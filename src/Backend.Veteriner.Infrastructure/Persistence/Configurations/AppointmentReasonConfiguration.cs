using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class AppointmentReasonConfiguration : IEntityTypeConfiguration<AppointmentReason>
{
    public void Configure(EntityTypeBuilder<AppointmentReason> b)
    {
        b.ToTable("AppointmentReasons");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(AppointmentReason.NameMaxLength);
        b.Property(x => x.DefaultDurationMinutes).IsRequired();
        b.Property(x => x.IsOnlineBookable).IsRequired();
        b.Property(x => x.SortOrder).IsRequired();
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.TenantId, x.ClinicId });

        // Aktif nedenler arasında ad klinik içinde benzersiz; pasif neden adı yeniden kullanılabilir.
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.Name })
            .IsUnique()
            .HasFilter("[IsActive] = 1");

        b.OwnsMany(x => x.Veterinarians, v =>
        {
            v.ToTable("AppointmentReasonVeterinarians");
            v.WithOwner().HasForeignKey("AppointmentReasonId");
            // Gölge kimlik anahtarı: koleksiyona eklenen satır her zaman INSERT olur (istemci anahtarı UPDATE sanılmaz).
            v.Property<int>("Id").ValueGeneratedOnAdd();
            v.HasKey("Id");
            v.Property(x => x.UserId).IsRequired();
            v.HasIndex("AppointmentReasonId", nameof(AppointmentReasonVeterinarian.UserId)).IsUnique();
        });
        b.Navigation(x => x.Veterinarians).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
