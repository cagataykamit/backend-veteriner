using Backend.Veteriner.Domain.Vaccinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class VaccinationConfiguration : IEntityTypeConfiguration<Vaccination>
{
    public void Configure(EntityTypeBuilder<Vaccination> b)
    {
        b.ToTable("Vaccinations");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ExaminationId);
        b.Property(x => x.VaccineName).IsRequired().HasMaxLength(300);
        b.Property(x => x.AppliedAtUtc);
        b.Property(x => x.DueAtUtc);
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.DueAtUtc });
        b.HasIndex(x => new { x.TenantId, x.AppliedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId });
    }
}
