using Backend.Veteriner.Domain.Treatments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class TreatmentConfiguration : IEntityTypeConfiguration<Treatment>
{
    public void Configure(EntityTypeBuilder<Treatment> b)
    {
        b.ToTable("Treatments");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.ExaminationId);
        b.Property(x => x.TreatmentDateUtc).IsRequired();

        b.Property(x => x.Title).IsRequired().HasMaxLength(500);
        b.Property(x => x.Description).IsRequired().HasMaxLength(8000);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.FollowUpDateUtc);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.TreatmentDateUtc });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId });
    }
}
