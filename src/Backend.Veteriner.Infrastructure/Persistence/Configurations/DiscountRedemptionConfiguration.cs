using Backend.Veteriner.Domain.SalesPartners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class DiscountRedemptionConfiguration : IEntityTypeConfiguration<DiscountRedemption>
{
    public const string TenantIndexName = "IX_DiscountRedemptions_TenantId";

    public void Configure(EntityTypeBuilder<DiscountRedemption> b)
    {
        b.ToTable("DiscountRedemptions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.DiscountCodeId).IsRequired();
        b.Property(x => x.SalesPartnerId).IsRequired();
        b.Property(x => x.Code).IsRequired().HasMaxLength(DiscountCodeText.MaxLength);
        b.Property(x => x.DiscountPercent).HasPrecision(5, 2).IsRequired();
        b.Property(x => x.CommissionPercent).HasPrecision(5, 2).IsRequired();
        b.Property(x => x.RedeemedAtUtc).IsRequired();
        b.Property(x => x.WindowStartsAtUtc);
        b.Property(x => x.WindowEndsAtUtc);

        // ADR-037 madde 5: kiracı başına tek kod.
        b.HasIndex(x => x.TenantId).IsUnique().HasDatabaseName(TenantIndexName);
        b.HasIndex(x => x.DiscountCodeId);
        b.HasIndex(x => x.SalesPartnerId);

        b.HasOne<DiscountCode>()
            .WithMany()
            .HasForeignKey(x => x.DiscountCodeId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<SalesPartner>()
            .WithMany()
            .HasForeignKey(x => x.SalesPartnerId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Collections)
            .WithOne()
            .HasForeignKey(c => c.DiscountRedemptionId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Collections).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.HasActiveCollection);
    }
}
