namespace Backend.Veteriner.Application.Reminders.Contracts.Requests;

public sealed record UpdateReminderSettingsRequest(
    bool AppointmentRemindersEnabled,
    int AppointmentReminderHoursBefore,
    bool VaccinationRemindersEnabled,
    int VaccinationReminderDaysBefore,
    bool EmailChannelEnabled);
