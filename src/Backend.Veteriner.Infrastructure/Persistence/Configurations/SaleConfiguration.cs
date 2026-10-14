using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Sales;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    /// <summary>Aynı istemci isteği (RequestId) kiracıda en fazla bir satış üretir.</summary>
    public const string RequestIdIndexName = "UX_Sales_TenantId_RequestId";

    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.ToTable("Sales", t =>
        {
            t.HasCheckConstraint("CK_Sales_TotalAmount_Positive", "[TotalAmount] > 0");
            t.HasCheckConstraint("CK_Sales_Void_Consistent", "([VoidedAtUtc] IS NULL AND [VoidReason] IS NULL AND [VoidedByUserId] IS NULL AND [VoidStockOutcome] IS NULL) OR ([VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL AND [VoidedByUserId] IS NOT NULL AND [VoidStockOutcome] IS NOT NULL)");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ClientId).IsRequired();
        b.Property(x => x.SoldAtUtc).IsRequired();
        b.Property(x => x.TotalAmount).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(Sale.MaxNotesLength);

        b.Property(x => x.ChargeId);
        b.Property(x => x.PaymentId);

        b.Property(x => x.VoidedAtUtc);
        b.Property(x => x.VoidedByUserId);
        b.Property(x => x.VoidReason).HasMaxLength(CorrectionReason.MaxLength);
        b.Property(x => x.VoidStockOutcome);

        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.RequestId).IsRequired();
        b.Property(x => x.RequestHash).IsRequired().HasMaxLength(64);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(l => l.SaleId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasOne<Charge>()
            .WithMany()
            .HasForeignKey(x => x.ChargeId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.SoldAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ClientId });

        b.HasIndex(x => new { x.TenantId, x.RequestId })
            .IsUnique()
            .HasDatabaseName(RequestIdIndexName);
    }
}
