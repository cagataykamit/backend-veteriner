using FluentValidation;

namespace Backend.Veteriner.Application.Reminders.Commands.UpdateSettings;

public sealed class UpdateReminderSettingsCommandValidator : AbstractValidator<UpdateReminderSettingsCommand>
{
    public UpdateReminderSettingsCommandValidator()
    {
        RuleFor(x => x.AppointmentReminderHoursBefore)
            .InclusiveBetween(1, 168)
            .WithMessage("appointmentReminderHoursBefore 1-168 aralığında olmalıdır.");

        RuleFor(x => x.VaccinationReminderDaysBefore)
            .InclusiveBetween(1, 30)
            .WithMessage("vaccinationReminderDaysBefore 1-30 aralığında olmalıdır.");
    }
}
