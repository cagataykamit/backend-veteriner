using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
{
    public void Configure(EntityTypeBuilder<TenantSubscription> b)
    {
        b.ToTable("TenantSubscriptions");

        b.HasKey(x => x.TenantId);

        b.Property(x => x.PlanCode)
            .IsRequired()
            .HasConversion<int>();

        b.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>();

        // Ödenen dönem türü (BILL-001); mevcut satırlar aylık.
        b.Property(x => x.BillingPeriod)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(BillingPeriodKind.Monthly);

        b.Property(x => x.TrialStartsAtUtc);
        b.Property(x => x.TrialEndsAtUtc);
        b.Property(x => x.ActivatedAtUtc);
        b.Property(x => x.CancelledAtUtc);

        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        // Deneme kohortu dönem filtresi (TRIAL-008).
        b.HasIndex(x => x.TrialStartsAtUtc);

        b.HasOne<Tenant>()
            .WithOne()
            .HasForeignKey<TenantSubscription>(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
