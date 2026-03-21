using Backend.Veteriner.Domain.Clinics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ClinicConfiguration : IEntityTypeConfiguration<Clinic>
{
    public void Configure(EntityTypeBuilder<Clinic> b)
    {
        b.ToTable("Clinics");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId)
            .IsRequired();

        b.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(300);

        b.Property(x => x.City)
            .IsRequired()
            .HasMaxLength(200);

        b.Property(x => x.IsActive)
            .IsRequired();

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.Name });
    }
}
