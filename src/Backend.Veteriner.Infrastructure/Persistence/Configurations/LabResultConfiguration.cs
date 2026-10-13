using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Shared;
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
        b.Property(x => x.VisitId);
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.InterpretationRequired).IsRequired();
        b.Property(x => x.RequestedAtUtc).IsRequired();
        b.Property(x => x.RequestedByUserId);
        b.Property(x => x.ResultDateUtc);

        b.Property(x => x.TestName).IsRequired().HasMaxLength(500);
        b.Property(x => x.ResultText).HasMaxLength(8000);
        b.Property(x => x.Interpretation).HasMaxLength(4000);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.CancelledAtUtc);
        b.Property(x => x.CancelledByUserId);
        b.Property(x => x.CancelReason).HasMaxLength(CorrectionReason.MaxLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.Ignore(x => x.AwaitingInterpretation);

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.RequestedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId });
    }
}
