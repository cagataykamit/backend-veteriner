using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ChargeSuggestionConfiguration : IEntityTypeConfiguration<ChargeSuggestion>
{
    /// <summary>Bir işlemden en çok bir kalem doğar (kiracıda kaynak benzersiz).</summary>
    public const string SourceIndexName = "UX_ChargeSuggestions_TenantId_SourceType_SourceId";

    /// <summary>Bir ücret en çok bir kalemi kapatır.</summary>
    public const string ChargeIndexName = "UX_ChargeSuggestions_TenantId_ChargeId";

    public void Configure(EntityTypeBuilder<ChargeSuggestion> b)
    {
        b.ToTable("ChargeSuggestions", t =>
        {
            t.HasCheckConstraint("CK_ChargeSuggestions_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_ChargeSuggestions_Status_Consistent",
                "([Status] = 0 AND [ChargeId] IS NULL AND [WaiveReason] IS NULL AND [ResolvedAtUtc] IS NULL)"
                + " OR ([Status] = 1 AND [ChargeId] IS NOT NULL AND [WaiveReason] IS NULL AND [ResolvedAtUtc] IS NOT NULL)"
                + " OR ([Status] = 2 AND [ChargeId] IS NULL AND [WaiveReason] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL)"
                + " OR ([Status] = 3 AND [ChargeId] IS NULL AND [WaiveReason] IS NULL AND [ResolvedAtUtc] IS NOT NULL)");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.ExaminationId);
        b.Property(x => x.VisitId);

        b.Property(x => x.SourceType).IsRequired().HasMaxLength(ChargeSuggestion.MaxSourceTypeLength);
        b.Property(x => x.SourceId).IsRequired();
        b.Property(x => x.ProductId).IsRequired();

        b.Property(x => x.Description).IsRequired().HasMaxLength(Charge.MaxDescriptionLength);
        b.Property(x => x.Quantity).HasPrecision(18, Charge.QuantityDecimals).IsRequired();
        b.Property(x => x.Unit).IsRequired().HasMaxLength(ChargeSuggestion.MaxUnitLength);
        b.Property(x => x.SuggestedUnitPrice).HasPrecision(18, Charge.UnitPriceDecimals);

        b.Property(x => x.OccurredAtUtc).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();

        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.ChargeId);
        b.Property(x => x.ResolvedAtUtc);
        b.Property(x => x.ResolvedByUserId);
        b.Property(x => x.WaiveReason).HasMaxLength(CorrectionReason.MaxLength);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.Ignore(x => x.IsPending);

        b.HasOne<Charge>()
            .WithMany()
            .HasForeignKey(x => x.ChargeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Bekleyen liste (klinik kapsamı, yeni → eski) ve muayene/geliş bazında sayım.
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.Status, x.OccurredAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.VisitId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.PetId, x.Status });

        b.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId })
            .IsUnique()
            .HasDatabaseName(SourceIndexName);

        b.HasIndex(x => new { x.TenantId, x.ChargeId })
            .IsUnique()
            .HasFilter("[ChargeId] IS NOT NULL")
            .HasDatabaseName(ChargeIndexName);
    }
}
