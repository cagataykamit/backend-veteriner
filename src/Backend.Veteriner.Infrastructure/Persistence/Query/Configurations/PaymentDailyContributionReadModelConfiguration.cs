using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Query.Configurations;

public sealed class PaymentDailyContributionReadModelConfiguration
    : IEntityTypeConfiguration<PaymentDailyContributionReadModel>
{
    public void Configure(EntityTypeBuilder<PaymentDailyContributionReadModel> b)
    {
        b.ToTable("PaymentDailyContributionReadModels");
        b.HasKey(x => x.PaymentId);

        b.Property(x => x.LocalDate).HasColumnType("date").IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.LastEventId).IsRequired();
        b.Property(x => x.LastEventOccurredAtUtc).IsRequired();
        b.Property(x => x.LastProjectedAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.LocalDate, x.Currency })
            .HasDatabaseName("IX_PaymentDailyContributionReadModels_Tenant_Clinic_LocalDate_Currency");
    }
}
