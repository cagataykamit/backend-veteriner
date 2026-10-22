using Backend.Veteriner.Domain.SalesPartners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class DiscountCodeConfiguration : IEntityTypeConfiguration<DiscountCode>
{
    public const string NormalizedCodeIndexName = "IX_DiscountCodes_NormalizedCode";

    public void Configure(EntityTypeBuilder<DiscountCode> b)
    {
        b.ToTable("DiscountCodes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.SalesPartnerId).IsRequired();
        b.Property(x => x.Code).IsRequired().HasMaxLength(DiscountCodeText.MaxLength);
        b.Property(x => x.NormalizedCode).IsRequired().HasMaxLength(DiscountCodeText.MaxLength);
        b.Property(x => x.DiscountPercent).HasPrecision(5, 2).IsRequired();
        b.Property(x => x.CommissionPercent).HasPrecision(5, 2).IsRequired();
        b.Property(x => x.ValidFromUtc);
        b.Property(x => x.ValidUntilUtc);
        b.Property(x => x.MaxRedemptions);
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.DeactivatedAtUtc);

        b.HasIndex(x => x.NormalizedCode).IsUnique().HasDatabaseName(NormalizedCodeIndexName);
        b.HasIndex(x => x.SalesPartnerId);

        b.HasOne<SalesPartner>()
            .WithMany()
            .HasForeignKey(x => x.SalesPartnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
