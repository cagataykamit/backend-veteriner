using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ProductStockConfiguration : IEntityTypeConfiguration<ProductStock>
{
    public void Configure(EntityTypeBuilder<ProductStock> b)
    {
        b.ToTable("ProductStocks");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ProductId).IsRequired();

        b.Property(x => x.QuantityOnHand)
            .HasPrecision(18, 3)
            .IsRequired();

        b.Property(x => x.MinimumStockLevel)
            .HasPrecision(18, 3)
            .IsRequired();

        b.Property(x => x.UpdatedAtUtc).IsRequired();

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.ProductId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.ProductId });

        b.HasOne(x => x.Product)
            .WithMany(p => p.Stocks)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
