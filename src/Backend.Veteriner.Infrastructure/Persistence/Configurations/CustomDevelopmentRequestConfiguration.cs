using Backend.Veteriner.Domain.CustomDevelopment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class CustomDevelopmentRequestConfiguration : IEntityTypeConfiguration<CustomDevelopmentRequest>
{
    public void Configure(EntityTypeBuilder<CustomDevelopmentRequest> b)
    {
        b.ToTable("CustomDevelopmentRequests", t =>
        {
            t.HasCheckConstraint(
                "CK_CustomDevelopmentRequests_OutOfScope_Reason",
                $"[Status] <> {(int)CustomDevelopmentRequestStatus.OutOfScope} OR [DecisionReason] IS NOT NULL");

            // Geçiş desteği (TIER-021) ayrıntıları yalnız o konuda ve hep birlikte dolu.
            t.HasCheckConstraint(
                "CK_CustomDevelopmentRequests_Migration_Details",
                $"([TargetKind] = {(int)CustomDevelopmentTargetKind.Migration} AND [MigrationSourceProgram] IS NOT NULL AND [MigrationVolume] IS NOT NULL "
                + "AND [MigrationDays] IS NOT NULL AND [MigrationTimes] IS NOT NULL AND [MigrationFee] IS NOT NULL) "
                + $"OR ([TargetKind] <> {(int)CustomDevelopmentTargetKind.Migration} AND [MigrationSourceProgram] IS NULL AND [MigrationVolume] IS NULL "
                + "AND [MigrationDays] IS NULL AND [MigrationTimes] IS NULL AND [MigrationFee] IS NULL)");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.RequestTitle).IsRequired().HasMaxLength(CustomDevelopmentRequest.MaxTitleLength);
        b.Property(x => x.RequestText).IsRequired().HasMaxLength(CustomDevelopmentRequest.MaxRequestTextLength);
        b.Property(x => x.TargetKind).IsRequired();
        b.Property(x => x.RequestTarget).IsRequired().HasMaxLength(CustomDevelopmentRequest.MaxTargetLength);
        b.Property(x => x.Priority).IsRequired();
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.DecisionReason).HasMaxLength(CustomDevelopmentRequest.MaxDecisionReasonLength);
        b.Property(x => x.ResponseNote).HasMaxLength(CustomDevelopmentRequest.MaxResponseNoteLength);
        b.Property(x => x.StatusChangedAtUtc);
        b.Property(x => x.StatusChangedByUserId);
        b.Property(x => x.MutationSequence).IsRequired().HasDefaultValue(0L).IsConcurrencyToken();
        b.Property(x => x.MigrationSourceProgram);
        b.Property(x => x.MigrationVolume);
        b.Property(x => x.MigrationDays);
        b.Property(x => x.MigrationTimes);
        b.Property(x => x.MigrationFee);
        b.Ignore(x => x.IsOpen);

        // Kiracı listesi ve açık talep sayımı (kiracı + durum); platform listesi (durum + zaman).
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.Status, x.CreatedAtUtc });
    }
}
