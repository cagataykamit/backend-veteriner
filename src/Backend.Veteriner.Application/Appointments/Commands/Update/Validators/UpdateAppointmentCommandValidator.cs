using FluentValidation;

namespace Backend.Veteriner.Application.Appointments.Commands.Update.Validators;

public sealed class UpdateAppointmentCommandValidator : AbstractValidator<UpdateAppointmentCommand>
{
    public UpdateAppointmentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
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