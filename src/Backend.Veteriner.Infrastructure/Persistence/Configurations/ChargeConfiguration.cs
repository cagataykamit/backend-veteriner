using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ChargeConfiguration : IEntityTypeConfiguration<Charge>
{
    /// <summary>Aynı istemci isteği (RequestId) kiracıda en fazla bir ücret üretir.</summary>
    public const string RequestIdIndexName = "UX_Charges_TenantId_RequestId";

    public void Configure(EntityTypeBuilder<Charge> b)
    {
        b.ToTable("Charges", t =>
        {
            t.HasCheckConstraint("CK_Charges_Amount_Positive", "[Amount] > 0");
            t.HasCheckConstraint("CK_Charges_AllocatedAmount_Range", "[AllocatedAmount] >= 0 AND [AllocatedAmount] <= [Amount]");
            t.HasCheckConstraint("CK_Charges_Void_Consistent", "([VoidedAtUtc] IS NULL AND [VoidReason] IS NULL AND [VoidedByUserId] IS NULL) OR ([VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL AND [VoidedByUserId] IS NOT NULL)");
            t.HasCheckConstraint("CK_Charges_Voided_Unallocated", "[VoidedAtUtc] IS NULL OR [AllocatedAmount] = 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ClientId).IsRequired();
        b.Property(x => x.PetId);
        b.Property(x => x.VisitId);
        b.Property(x => x.ExaminationId);

        b.Property(x => x.Description).IsRequired().HasMaxLength(Charge.MaxDescriptionLength);
        b.Property(x => x.Quantity).HasPrecision(18, Charge.QuantityDecimals).IsRequired();
        b.Property(x => x.UnitPrice).HasPrecision(18, Charge.UnitPriceDecimals).IsRequired();
        b.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.Currency).IsRequired().HasMaxLength(3);

        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.RequestId);

        b.Property(x => x.AllocatedAmount).HasPrecision(18, 2).IsRequired().HasDefaultValue(0m);
        b.Property(x => x.MutationSequence).IsRequired().HasDefaultValue(0L).IsConcurrencyToken();

        b.Property(x => x.VoidedAtUtc);
        b.Property(x => x.VoidedByUserId);
        b.Property(x => x.VoidReason).HasMaxLength(CorrectionReason.MaxLength);

        // İptal edilen ücret tüm okuma yüzeylerinden tek yerde düşer; iptal ucu ve requestId araması IgnoreQueryFilters kullanır.
        b.HasQueryFilter(x => x.VoidedAtUtc == null);

        b.HasIndex(x => new { x.TenantId, x.ClientId });
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.VisitId });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId });

        b.HasIndex(x => new { x.TenantId, x.RequestId })
            .IsUnique()
            .HasFilter("[RequestId] IS NOT NULL")
            .HasDatabaseName(RequestIdIndexName);
    }
}
