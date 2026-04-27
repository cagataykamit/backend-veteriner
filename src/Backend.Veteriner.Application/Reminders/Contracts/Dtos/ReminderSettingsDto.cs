namespace Backend.Veteriner.Application.Reminders.Contracts.Dtos;

public sealed record ReminderSettingsDto(
    bool AppointmentRemindersEnabled,
    int AppointmentReminderHoursBefore,
    bool VaccinationRemindersEnabled,
    int VaccinationReminderDaysBefore,
    bool EmailChannelEnabled,
    DateTime? UpdatedAtUtc);
