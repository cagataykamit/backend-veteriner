using Backend.Veteriner.Application.Reminders.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reminders.Commands.UpdateSettings;

public sealed record UpdateReminderSettingsCommand(
    bool AppointmentRemindersEnabled,
    int AppointmentReminderHoursBefore,
    bool VaccinationRemindersEnabled,
    int VaccinationReminderDaysBefore,
    bool EmailChannelEnabled)
    : IRequest<Result<ReminderSettingsDto>>;
