using Backend.Veteriner.Domain.Hospitalizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class HospitalizationConfiguration : IEntityTypeConfiguration<Hospitalization>
{
    public void Configure(EntityTypeBuilder<Hospitalization> b)
    {
        b.ToTable("Hospitalizations");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.ExaminationId);
        b.Property(x => x.AdmittedAtUtc).IsRequired();
        b.Property(x => x.PlannedDischargeAtUtc);
        b.Property(x => x.DischargedAtUtc);

        b.Property(x => x.Reason).IsRequired().HasMaxLength(2000);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.AdmittedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId });

        // At most one active (not discharged) stay per pet per clinic within a tenant.
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.PetId })
            .IsUnique()
            .HasFilter("[DischargedAtUtc] IS NULL");
    }
}
