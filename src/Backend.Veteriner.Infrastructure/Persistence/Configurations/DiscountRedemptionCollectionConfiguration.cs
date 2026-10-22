using Backend.Veteriner.Domain.SalesPartners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class DiscountRedemptionCollectionConfiguration : IEntityTypeConfiguration<DiscountRedemptionCollection>
{
    public void Configure(EntityTypeBuilder<DiscountRedemptionCollection> b)
    {
        b.ToTable("DiscountRedemptionCollections");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.DiscountRedemptionId).IsRequired();
        b.Property(x => x.CheckoutSessionId).IsRequired();
        b.Property(x => x.BillingPeriod).IsRequired().HasConversion<int>();
        b.Property(x => x.CollectedAtUtc).IsRequired();
        b.Property(x => x.GrossAmountMinor).IsRequired();
        b.Property(x => x.DiscountAmountMinor).IsRequired();
        b.Property(x => x.NetAmountMinor).IsRequired();
        b.Property(x => x.CommissionAmountMinor).IsRequired();
        b.Property(x => x.Status).IsRequired().HasConversion<int>();
        b.Property(x => x.PaidAtUtc);
        b.Property(x => x.PaidNote).HasMaxLength(DiscountRedemptionCollection.MaxNoteLength);
        b.Property(x => x.CancelledAtUtc);
        b.Property(x => x.CancelReason).HasMaxLength(DiscountRedemptionCollection.MaxNoteLength);

        b.Ignore(x => x.IsActive);
        b.Ignore(x => x.IsClawback);

        // Aynı tahsilat olayı iki kez komisyon üretmez.
        b.HasIndex(x => x.CheckoutSessionId).IsUnique();
        b.HasIndex(x => x.DiscountRedemptionId);
    }
}
