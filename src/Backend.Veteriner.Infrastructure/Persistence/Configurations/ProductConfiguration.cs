using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ProductCategoryId);
        b.Property(x => x.Name).IsRequired().HasMaxLength(300);
        b.Property(x => x.Sku).HasMaxLength(100);
        b.Property(x => x.Barcode).HasMaxLength(Product.BarcodeMaxLength);
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.Unit).IsRequired().HasMaxLength(50);

        b.Property(x => x.UnitPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        b.Property(x => x.Currency).IsRequired().HasMaxLength(3);

        b.Property(x => x.Kind).HasDefaultValue(ProductKind.Other).IsRequired();
        b.Property(x => x.ControlledClass).HasDefaultValue(ControlledSubstanceClass.None).IsRequired();
        b.Property(x => x.PharmacologicalForm).HasMaxLength(100);
        b.Property(x => x.PresentationForm).HasMaxLength(100);
        b.Property(x => x.Packaging).HasMaxLength(100);
        b.Property(x => x.Manufacturer).HasMaxLength(Product.ManufacturerMaxLength);

        // BATCH_STOCK_API_CONTRACT.md §2.1: mevcut satırlar boş kalır (KDV = klinik varsayılanı).
        b.Property(x => x.VatRate).HasPrecision(5, TaxRatePercent.Decimals);
        b.Property(x => x.PurchasePrice).HasPrecision(18, Product.PurchasePriceDecimals);

        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.Name });
        b.HasIndex(x => new { x.TenantId, x.IsActive });
        b.HasIndex(x => x.ProductCategoryId);

        b.HasIndex(x => new { x.TenantId, x.Sku })
            .IsUnique()
            .HasFilter("[Sku] IS NOT NULL");

        // TIER-006: barkod kiracı içinde benzersiz; adı ProductErrors.IsDuplicateBarcodeViolation ile eşleşir.
        b.HasIndex(x => new { x.TenantId, x.Barcode })
            .IsUnique()
            .HasFilter("[Barcode] IS NOT NULL");

        b.HasOne(x => x.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(x => x.ProductCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
