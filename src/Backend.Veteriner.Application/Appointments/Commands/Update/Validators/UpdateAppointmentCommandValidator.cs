using Backend.Veteriner.Domain.Appointments;
using FluentValidation;

namespace Backend.Veteriner.Application.Appointments.Commands.Update.Validators;

public sealed class UpdateAppointmentCommandValidator : AbstractValidator<UpdateAppointmentCommand>
{
    public UpdateAppointmentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
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
            .Must(Enum.IsDefined<AppointmentStatus>)
            .WithMessage("Randevu durumu ge�ersiz.");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}