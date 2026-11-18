using System.Text.Json;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Clinics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
        b.Property(x => x.SourceRefs).HasColumnName("SourceRefsJson").IsRequired().AiSourceRefsAsJson();
        b.Property(x => x.DraftId);
        b.Property(x => x.Model).IsRequired().HasMaxLength(AiUsageRecord.ModelMaxLength);
        b.Property(x => x.PromptVersion).IsRequired().HasMaxLength(AiUsageRecord.PromptVersionMaxLength);
        b.Property(x => x.InputTokens).IsRequired();
        b.Property(x => x.OutputTokens).IsRequired();
        b.Property(x => x.CostUsd).IsRequired().HasPrecision(18, 6);
        b.Property(x => x.Credits).IsRequired().HasPrecision(18, 2);
        b.Property(x => x.FundCredits).IsRequired().HasPrecision(18, 2);
        b.Property(x => x.DurationMs).IsRequired();
        b.Property(x => x.Outcome).IsRequired().HasConversion<int>();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Ignore(x => x.TotalTokens);

        // Sınır denetimi: kullanıcının son dakikası, kliniğin ve sistemin bu ayı.
        b.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.CreatedAtUtc });
        b.HasIndex(x => x.CreatedAtUtc);
        b.HasIndex(x => x.DraftId).HasFilter("[DraftId] IS NOT NULL");

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AiDraft>().WithMany().HasForeignKey(x => x.DraftId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>AI taslağı (AI-009). İçerik yalnız karar bekleyen taslakta doludur; karar alanları denetim için kalır (AI-010).</summary>
public sealed class AiDraftConfiguration : IEntityTypeConfiguration<AiDraft>
{
    public void Configure(EntityTypeBuilder<AiDraft> b)
    {
        b.ToTable("AiDrafts");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.Feature).IsRequired().HasConversion<int>();
        b.Property(x => x.TargetType).IsRequired().HasMaxLength(AiDraft.TargetTypeMaxLength);
        b.Property(x => x.TargetId).IsRequired();
        b.Property(x => x.SourceRefs).HasColumnName("SourceRefsJson").IsRequired().AiSourceRefsAsJson();
        b.Property(x => x.Content).HasColumnName("ContentJson").HasMaxLength(AiDraft.ContentMaxLength);
        b.Property(x => x.Model).IsRequired().HasMaxLength(AiUsageRecord.ModelMaxLength);
        b.Property(x => x.PromptVersion).IsRequired().HasMaxLength(AiUsageRecord.PromptVersionMaxLength);
        b.Property(x => x.Status).IsRequired().HasConversion<int>();
        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.ExpiresAtUtc).IsRequired();
        b.Property(x => x.DecidedByUserId);
        b.Property(x => x.DecidedAtUtc);
        b.Property(x => x.AppliedRecordId);
        b.Property(x => x.RowVersion).IsRowVersion();

        // Bir kaydın bekleyen taslakları; süre taraması.
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.TargetType, x.TargetId, x.Status });
        b.HasIndex(x => new { x.Status, x.ExpiresAtUtc });
        // Kayıttaki "AI taslağından" izi (AiProvenanceReader).
        b.HasIndex(x => new { x.TenantId, x.AppliedRecordId }).HasFilter("[AppliedRecordId] IS NOT NULL");

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal static class AiSourceRefsJsonConversion
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Kaynak kimlikleri tek sütunda JSON dizi (yalnız tür + kimlik); sorgulanmaz, yalnız denetimde okunur.</summary>
    public static PropertyBuilder<IReadOnlyList<AiSourceRef>> AiSourceRefsAsJson(this PropertyBuilder<IReadOnlyList<AiSourceRef>> property)
        => property.HasConversion(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<List<AiSourceRef>>(v, JsonOptions) ?? new List<AiSourceRef>(),
            new ValueComparer<IReadOnlyList<AiSourceRef>>(
                (a, c) => a!.SequenceEqual(c!),
                v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode())),
                v => v.ToList()));
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
        b.Property(x => x.QuotaWarningMonthStartUtc);
        b.Property(x => x.QuotaWarningLevel).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ClinicId }).IsUnique();

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>AI fon hareketi (ADR-044 madde 6); bakiye toplamdan hesaplanır.</summary>
public sealed class AiCreditGrantConfiguration : IEntityTypeConfiguration<AiCreditGrant>
{
    public void Configure(EntityTypeBuilder<AiCreditGrant> b)
    {
        b.ToTable("AiCreditGrants");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.Kind).IsRequired().HasConversion<int>();
        b.Property(x => x.Credits).IsRequired();
        b.Property(x => x.Note).HasMaxLength(AiCreditGrant.NoteMaxLength);
        b.Property(x => x.CreatedByUserId);
        b.Property(x => x.CreatedAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.CreatedAtUtc });

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
    }
}
