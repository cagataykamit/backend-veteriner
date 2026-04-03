using Backend.Veteriner.Domain.LabResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class LabResultConfiguration : IEntityTypeConfiguration<LabResult>
{
    public void Configure(EntityTypeBuilder<LabResult> b)
    {
        b.ToTable("LabResults");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.ExaminationId);
        b.Property(x => x.ResultDateUtc).IsRequired();

        b.Property(x => x.TestName).IsRequired().HasMaxLength(500);
        b.Property(x => x.ResultText).IsRequired().HasMaxLength(8000);
        b.Property(x => x.Interpretation).HasMaxLength(4000);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.ResultDateUtc });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId });
    }
}
