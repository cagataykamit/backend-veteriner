using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Hospitalizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class HospitalizationWardConfiguration : IEntityTypeConfiguration<HospitalizationWard>
{
    public void Configure(EntityTypeBuilder<HospitalizationWard> b)
    {
        b.ToTable("HospitalizationWards");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(HospitalizationWard.NameMaxLength);
        b.Property(x => x.SortOrder).IsRequired();
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.TenantId, x.ClinicId });

        // Aktif bölümler arasında ad klinik içinde benzersiz; pasif bölüm adı yeniden kullanılabilir.
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.Name })
            .IsUnique()
            .HasFilter("[IsActive] = 1");

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
