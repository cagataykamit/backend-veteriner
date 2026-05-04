using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Commands.AppointmentSettings.UpdateClinicAppointmentSettings.Validators;

public sealed class UpdateClinicAppointmentSettingsCommandValidator : AbstractValidator<UpdateClinicAppointmentSettingsCommand>
{
    public UpdateClinicAppointmentSettingsCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.DefaultAppointmentDurationMinutes).InclusiveBetween(5, 240);
        RuleFor(x => x.SlotIntervalMinutes).InclusiveBetween(5, 120);
    }
}
