using Backend.Veteriner.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class BillingCheckoutSessionConfiguration : IEntityTypeConfiguration<BillingCheckoutSession>
{
    public void Configure(EntityTypeBuilder<BillingCheckoutSession> b)
    {
        b.ToTable("BillingCheckoutSessions");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.CurrentPlanCode)
            .IsRequired()
            .HasConversion<int>();

        b.Property(x => x.TargetPlanCode)
            .IsRequired()
            .HasConversion<int>();

        b.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>();

        b.Property(x => x.Provider)
            .IsRequired()
            .HasConversion<int>();

        b.Property(x => x.BillingPeriod)
            .IsRequired()
            .HasConversion<int>();

        b.Property(x => x.ExternalReference)
            .HasMaxLength(250);

        b.Property(x => x.CheckoutUrl)
            .HasMaxLength(1000);

        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.ExpiresAtUtc);
        b.Property(x => x.CompletedAtUtc);
        b.Property(x => x.FailedAtUtc);
        b.Property(x => x.GrossAmountMinor);
        b.Property(x => x.DiscountAmountMinor);

        b.Property(x => x.RefundStatus)
            .IsRequired()
            .HasConversion<int>();
        b.Property(x => x.DuplicatePaymentDetectedAtUtc);
        b.Property(x => x.RefundedAtUtc);
        b.Property(x => x.RefundAttemptCount).IsRequired();
        b.Property(x => x.RefundFailureReason)
            .HasMaxLength(BillingCheckoutSession.RefundFailureReasonMaxLength);

        b.HasIndex(x => new { x.TenantId, x.Status, x.ExpiresAtUtc })
            .HasDatabaseName("IX_BillingCheckoutSessions_Tenant_OpenStatus");

        // Bekleyen iadeleri arka plan işi, tüm çift ödemeleri yönetici ekranı tarar (BILL-005).
        b.HasIndex(x => x.RefundStatus)
            .HasDatabaseName("IX_BillingCheckoutSessions_RefundStatus")
            .HasFilter("[RefundStatus] <> 0");

        b.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

