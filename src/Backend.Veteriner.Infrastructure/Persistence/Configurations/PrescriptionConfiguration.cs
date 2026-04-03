using Backend.Veteriner.Domain.Prescriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> b)
    {
        b.ToTable("Prescriptions");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.ExaminationId);
        b.Property(x => x.TreatmentId);
        b.Property(x => x.PrescribedAtUtc).IsRequired();

        b.Property(x => x.Title).IsRequired().HasMaxLength(500);
        b.Property(x => x.Content).IsRequired().HasMaxLength(8000);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.FollowUpDateUtc);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.PrescribedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId });
        b.HasIndex(x => new { x.TenantId, x.TreatmentId });
    }
}
