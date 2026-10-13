using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class StockBatchConfiguration : IEntityTypeConfiguration<StockBatch>
{
    public void Configure(EntityTypeBuilder<StockBatch> b)
    {
        b.ToTable("StockBatches", t =>
        {
            t.HasCheckConstraint("CK_StockBatches_Quantities", "[InitialQuantity] > 0 AND [RemainingQuantity] >= 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ProductId).IsRequired();

        b.Property(x => x.LotNumber).HasMaxLength(StockBatch.MaxLotNumberLength);
        b.Property(x => x.ExpiryDate);

        b.Property(x => x.InitialQuantity).HasPrecision(18, 3).IsRequired();
        b.Property(x => x.RemainingQuantity).HasPrecision(18, 3).IsRequired();

        b.Property(x => x.SupplierName).HasMaxLength(StockBatch.MaxSupplierNameLength);
        b.Property(x => x.InvoiceNumber).HasMaxLength(StockBatch.MaxInvoiceNumberLength);
        b.Property(x => x.InvoiceDate);
        b.Property(x => x.ReceivedDate);
        b.Property(x => x.DispatchedDate);
        b.Property(x => x.TransportMethod).HasMaxLength(StockBatch.MaxTransportLength);
        b.Property(x => x.ColdChainMonitor).HasMaxLength(StockBatch.MaxTransportLength);

        b.Property(x => x.CreatedByUserId);
        b.Property(x => x.CreatedAtUtc).IsRequired();

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.ProductId, x.ExpiryDate });
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.LotNumber });

        b.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
