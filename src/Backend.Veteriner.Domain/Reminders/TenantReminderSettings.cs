using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Reminders;

public sealed class TenantReminderSettings : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public bool AppointmentRemindersEnabled { get; private set; }
    public int AppointmentReminderHoursBefore { get; private set; }
    public bool VaccinationRemindersEnabled { get; private set; }
    public int VaccinationReminderDaysBefore { get; private set; }
    public bool EmailChannelEnabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private TenantReminderSettings() { }

    public static TenantReminderSettings CreateDefault(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));

        return new TenantReminderSettings
        {
            TenantId = tenantId,
            AppointmentRemindersEnabled = false,
            AppointmentReminderHoursBefore = 24,
            VaccinationRemindersEnabled = false,
            VaccinationReminderDaysBefore = 3,
            EmailChannelEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = null
        };
    }

    public Result Update(
        bool appointmentRemindersEnabled,
        int appointmentReminderHoursBefore,
        bool vaccinationRemindersEnabled,
        int vaccinationReminderDaysBefore,
        bool emailChannelEnabled)
    {
        if (appointmentReminderHoursBefore < 1 || appointmentReminderHoursBefore > 168)
            return Result.Failure("Reminders.Settings.Validation.AppointmentReminderHoursBefore", "appointmentReminderHoursBefore 1-168 aralığında olmalıdır.");
        if (vaccinationReminderDaysBefore < 1 || vaccinationReminderDaysBefore > 30)
            return Result.Failure("Reminders.Settings.Validation.VaccinationReminderDaysBefore", "vaccinationReminderDaysBefore 1-30 aralığında olmalıdır.");

        AppointmentRemindersEnabled = appointmentRemindersEnabled;
        AppointmentReminderHoursBefore = appointmentReminderHoursBefore;
        VaccinationRemindersEnabled = vaccinationRemindersEnabled;
        VaccinationReminderDaysBefore = vaccinationReminderDaysBefore;
        EmailChannelEnabled = emailChannelEnabled;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }
}
