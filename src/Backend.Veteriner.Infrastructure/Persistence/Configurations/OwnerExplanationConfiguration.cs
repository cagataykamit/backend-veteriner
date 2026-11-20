using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.OwnerExplanations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>Hasta sahibine açıklama (AI-003): muayeneye bağlı, oluşturulduktan sonra değişmez.</summary>
public sealed class OwnerExplanationConfiguration : IEntityTypeConfiguration<OwnerExplanation>
{
    public void Configure(EntityTypeBuilder<OwnerExplanation> b)
    {
        b.ToTable("OwnerExplanations");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.ExaminationId).IsRequired();
        b.Property(x => x.Text).IsRequired().HasMaxLength(OwnerExplanation.MaxTextLength);
        b.Property(x => x.SourceAiDraftId);
        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ExaminationId });
        b.HasIndex(x => new { x.TenantId, x.PetId });

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Examination>().WithMany().HasForeignKey(x => x.ExaminationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AiDraft>().WithMany().HasForeignKey(x => x.SourceAiDraftId).OnDelete(DeleteBehavior.Restrict);
    }
}
