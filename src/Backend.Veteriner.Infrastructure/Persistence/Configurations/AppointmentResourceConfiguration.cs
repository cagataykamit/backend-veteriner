using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class AppointmentResourceConfiguration : IEntityTypeConfiguration<AppointmentResource>
{
    public void Configure(EntityTypeBuilder<AppointmentResource> b)
    {
        b.ToTable("AppointmentResources");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(AppointmentResource.NameMaxLength);
        b.Property(x => x.Type).IsRequired().HasConversion<int>();
        b.Property(x => x.Capacity).IsRequired();
        b.Property(x => x.SortOrder).IsRequired();
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.TenantId, x.ClinicId });

        // Aktif kaynaklar arasında ad klinik içinde benzersiz; pasif kaynak adı yeniden kullanılabilir.
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.Name })
            .IsUnique()
            .HasFilter("[IsActive] = 1");

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
