using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> b)
    {
        b.ToTable("SaleLines", t =>
        {
            t.HasCheckConstraint("CK_SaleLines_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_SaleLines_LineAmount_Positive", "[LineAmount] > 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.SaleId).IsRequired();
        b.Property(x => x.ProductId).IsRequired();
        b.Property(x => x.ProductName).IsRequired().HasMaxLength(300);
        b.Property(x => x.Unit).IsRequired().HasMaxLength(50);
        b.Property(x => x.Quantity).HasPrecision(18, 3).IsRequired();
        b.Property(x => x.UnitPrice).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.LineAmount).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.BatchId);

        b.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<StockBatch>()
            .WithMany()
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.SaleId);
        b.HasIndex(x => x.ProductId);
    }
}
