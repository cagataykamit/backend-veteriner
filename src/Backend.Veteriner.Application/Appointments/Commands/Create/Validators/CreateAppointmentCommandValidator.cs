using Backend.Veteriner.Application.Appointments.Commands.Create;
using FluentValidation;

namespace Backend.Veteriner.Application.Appointments.Commands.Create.Validators;

public sealed class CreateAppointmentCommandValidator : AbstractValidator<CreateAppointmentCommand>
{
    public CreateAppointmentCommandValidator()
    {
        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ClinicId geçersiz.");

        RuleFor(x => x.PetId).NotEmpty();

        RuleFor(x => x.ScheduledAtUtc)
            .Must(d => d != default)
            .WithMessage("Randevu zamaný zorunludur.");

        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}