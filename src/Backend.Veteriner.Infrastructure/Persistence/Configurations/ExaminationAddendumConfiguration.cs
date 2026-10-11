using Backend.Veteriner.Domain.Examinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ExaminationAddendumConfiguration : IEntityTypeConfiguration<ExaminationAddendum>
{
    public void Configure(EntityTypeBuilder<ExaminationAddendum> b)
    {
        b.ToTable("ExaminationAddenda");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ExaminationId).IsRequired();
        b.Property(x => x.Reason).IsRequired().HasMaxLength(ExaminationAddendum.MaxReasonLength);
        b.Property(x => x.Text).IsRequired().HasMaxLength(ExaminationAddendum.MaxTextLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.CreatedByUserId).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ExaminationId, x.CreatedAtUtc });
    }
}
