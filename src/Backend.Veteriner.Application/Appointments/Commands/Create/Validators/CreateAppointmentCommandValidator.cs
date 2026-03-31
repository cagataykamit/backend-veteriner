using Backend.Veteriner.Application.Appointments.Commands.Create;
using Backend.Veteriner.Domain.Appointments;
using FluentValidation;

namespace Backend.Veteriner.Application.Appointments.Commands.Create.Validators;

public sealed class CreateAppointmentCommandValidator : AbstractValidator<CreateAppointmentCommand>
{
    public CreateAppointmentCommandValidator()
    {
        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ClinicId ge�ersiz.");

        RuleFor(x => x.PetId).NotEmpty();

        RuleFor(x => x.ScheduledAtUtc)
            .Must(d => d != default)
            .WithMessage("Randevu zaman� zorunludur.");

        RuleFor(x => x.AppointmentType)
            .Must(Enum.IsDefined<AppointmentType>)
            .WithMessage("Randevu t�r� ge�ersiz.");

        RuleFor(x => x.Status)
            .Must(s => !s.HasValue || Enum.IsDefined(s.Value))
            .WithMessage("Randevu durumu ge�ersiz.");

        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}