using Backend.Veteriner.Application.Appointments.Commands.Create;
using FluentValidation;

namespace Backend.Veteriner.Application.Appointments.Commands.Create.Validators;

public sealed class CreateAppointmentCommandValidator : AbstractValidator<CreateAppointmentCommand>
{
    public CreateAppointmentCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.ScheduledAtUtc)
            .Must(d => d != default)
            .WithMessage("Randevu zamanı zorunludur.");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
