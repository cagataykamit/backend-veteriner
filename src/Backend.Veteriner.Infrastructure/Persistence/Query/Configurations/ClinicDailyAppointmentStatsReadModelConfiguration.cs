using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Query.Configurations;

public sealed class ClinicDailyAppointmentStatsReadModelConfiguration
    : IEntityTypeConfiguration<ClinicDailyAppointmentStatsReadModel>
{
    public void Configure(EntityTypeBuilder<ClinicDailyAppointmentStatsReadModel> b)
    {
        b.ToTable("ClinicDailyAppointmentStatsReadModels");
        b.HasKey(x => new { x.TenantId, x.ClinicId, x.LocalDate });

        b.Property(x => x.LocalDate).HasColumnType("date").IsRequired();
        b.Property(x => x.ScheduledCount).IsRequired();
        b.Property(x => x.CompletedCount).IsRequired();
        b.Property(x => x.CancelledCount).IsRequired();
        b.Property(x => x.TotalCount).IsRequired();
        b.Property(x => x.LastEventId).IsRequired();
        b.Property(x => x.LastProjectedAtUtc).IsRequired();
    }
}
