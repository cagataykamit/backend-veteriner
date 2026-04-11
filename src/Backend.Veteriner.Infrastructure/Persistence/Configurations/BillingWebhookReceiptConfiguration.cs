using Backend.Veteriner.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class BillingWebhookReceiptConfiguration : IEntityTypeConfiguration<BillingWebhookReceipt>
{
    public void Configure(EntityTypeBuilder<BillingWebhookReceipt> b)
    {
        b.ToTable("BillingWebhookReceipts");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Provider)
            .IsRequired()
            .HasConversion<int>();

        b.Property(x => x.ProviderEventId)
            .IsRequired()
            .HasMaxLength(200);

        b.Property(x => x.EventType).HasMaxLength(200);
        b.Property(x => x.CorrelationId).HasMaxLength(200);

        b.Property(x => x.ReceivedAtUtc).IsRequired();
        b.Property(x => x.ProcessedAtUtc);

        b.HasIndex(x => new { x.Provider, x.ProviderEventId })
            .IsUnique()
            .HasDatabaseName("IX_BillingWebhookReceipts_Provider_EventId");
    }
}
