using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Clinics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class AiUsageRecordConfiguration : IEntityTypeConfiguration<AiUsageRecord>
{
    public void Configure(EntityTypeBuilder<AiUsageRecord> b)
    {
        b.ToTable("AiUsageRecords");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.Feature).IsRequired().HasConversion<int>();
        b.Property(x => x.SubjectId);
        b.Property(x => x.Model).IsRequired().HasMaxLength(AiUsageRecord.ModelMaxLength);
        b.Property(x => x.InputTokens).IsRequired();
        b.Property(x => x.OutputTokens).IsRequired();
        b.Property(x => x.CostUsd).IsRequired().HasPrecision(18, 6);
        b.Property(x => x.DurationMs).IsRequired();
        b.Property(x => x.Outcome).IsRequired().HasConversion<int>();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Ignore(x => x.TotalTokens);

        // Sınır denetimi: kullanıcının son dakikası, kliniğin ve sistemin bu ayı.
        b.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.CreatedAtUtc });
        b.HasIndex(x => x.CreatedAtUtc);

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ClinicAiSettingsConfiguration : IEntityTypeConfiguration<ClinicAiSettings>
{
    public void Configure(EntityTypeBuilder<ClinicAiSettings> b)
    {
        b.ToTable("ClinicAiSettings");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.IsEnabled).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.UpdatedByUserId);

        b.HasIndex(x => new { x.TenantId, x.ClinicId }).IsUnique();

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
    }
}
