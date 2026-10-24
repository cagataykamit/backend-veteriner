using Backend.Veteriner.Domain.Hospitalizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class HospitalizationRoomConfiguration : IEntityTypeConfiguration<HospitalizationRoom>
{
    public void Configure(EntityTypeBuilder<HospitalizationRoom> b)
    {
        b.ToTable("HospitalizationRooms");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.WardId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(HospitalizationRoom.NameMaxLength);
        b.Property(x => x.SortOrder).IsRequired();
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.WardId });

        // Aktif odalar arasında ad bölüm içinde benzersiz; pasif oda adı yeniden kullanılabilir.
        b.HasIndex(x => new { x.TenantId, x.WardId, x.Name })
            .IsUnique()
            .HasFilter("[IsActive] = 1");

        b.HasOne<HospitalizationWard>()
            .WithMany()
            .HasForeignKey(x => x.WardId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
