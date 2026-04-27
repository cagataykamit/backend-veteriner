using Backend.Veteriner.Domain.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ReminderDispatchLogConfiguration : IEntityTypeConfiguration<ReminderDispatchLog>
{
    public void Configure(EntityTypeBuilder<ReminderDispatchLog> b)
    {
        b.ToTable("ReminderDispatchLogs");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId);
        b.Property(x => x.ReminderType).IsRequired().HasConversion<int>();
        b.Property(x => x.SourceEntityType).IsRequired().HasConversion<int>();
        b.Property(x => x.SourceEntityId).IsRequired();
        b.Property(x => x.RecipientEmail).IsRequired().HasMaxLength(320);
        b.Property(x => x.RecipientName).IsRequired().HasMaxLength(300);
        b.Property(x => x.ScheduledForUtc).IsRequired();
        b.Property(x => x.ReminderDueAtUtc).IsRequired();
        b.Property(x => x.Status).IsRequired().HasConversion<int>();
        b.Property(x => x.DedupeKey).IsRequired().HasMaxLength(200);
        b.Property(x => x.OutboxMessageId);
        b.Property(x => x.SentAtUtc);
        b.Property(x => x.FailedAtUtc);
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.TenantId, x.DedupeKey }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ReminderType, x.Status });
        b.HasIndex(x => new { x.TenantId, x.SourceEntityType, x.SourceEntityId });
    }
}
