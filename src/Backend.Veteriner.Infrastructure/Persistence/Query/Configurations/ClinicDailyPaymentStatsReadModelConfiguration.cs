using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Query.Configurations;

public sealed class ClinicDailyPaymentStatsReadModelConfiguration
    : IEntityTypeConfiguration<ClinicDailyPaymentStatsReadModel>
{
    public void Configure(EntityTypeBuilder<ClinicDailyPaymentStatsReadModel> b)
    {
        b.ToTable("ClinicDailyPaymentStatsReadModels");
        b.HasKey(x => new { x.TenantId, x.ClinicId, x.LocalDate, x.Currency });

        b.Property(x => x.LocalDate).HasColumnType("date").IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.PaidTotalAmount).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.PaidCount).IsRequired();
        b.Property(x => x.LastEventId).IsRequired();
        b.Property(x => x.LastEventOccurredAtUtc).IsRequired();
        b.Property(x => x.LastProjectedAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.LocalDate })
            .HasDatabaseName("IX_ClinicDailyPaymentStatsReadModels_TenantId_LocalDate");
    }
}
