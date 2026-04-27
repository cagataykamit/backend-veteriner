using Backend.Veteriner.Domain.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class TenantReminderSettingsConfiguration : IEntityTypeConfiguration<TenantReminderSettings>
{
    public void Configure(EntityTypeBuilder<TenantReminderSettings> b)
    {
        b.ToTable("TenantReminderSettings");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.AppointmentRemindersEnabled).IsRequired();
        b.Property(x => x.AppointmentReminderHoursBefore).IsRequired();
        b.Property(x => x.VaccinationRemindersEnabled).IsRequired();
        b.Property(x => x.VaccinationReminderDaysBefore).IsRequired();
        b.Property(x => x.EmailChannelEnabled).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.TenantId).IsUnique();
    }
}
