using Backend.Veteriner.Domain.CareInstructions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class CareInstructionConfiguration : IEntityTypeConfiguration<CareInstruction>
{
    public void Configure(EntityTypeBuilder<CareInstruction> b)
    {
        b.ToTable("CareInstructions");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.HospitalizationId);
        b.Property(x => x.ExaminationId);
        b.Property(x => x.VisitId);

        b.Property(x => x.Text).IsRequired().HasMaxLength(CareInstruction.MaxTextLength);
        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedByUserId);
        b.Property(x => x.UpdatedAtUtc);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.HospitalizationId });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId });
        b.HasIndex(x => new { x.TenantId, x.VisitId });
    }
}
