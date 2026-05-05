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
            .WithMessage("ClinicId geçersiz.");
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.ScheduledAtUtc)
            .Must(d => d != default)
            .WithMessage("Randevu zamanı zorunludur.");
        RuleFor(x => x.AppointmentType)
            .Must(Enum.IsDefined<AppointmentType>)
            .WithMessage("Randevu türü geçersiz.");
        RuleFor(x => x.Status)
            .Must(Enum.IsDefined<AppointmentStatus>)
            .WithMessage("Randevu durumu geçersiz.");
        RuleFor(x => x.Notes).MaximumLength(2000);

        RuleFor(x => x.DurationMinutes)
            .Must(d => !d.HasValue || Appointment.IsValidDurationMinutes(d.Value))
            .WithMessage("Randevu süresi 5-240 dakika arasında olmalıdır.");
    }
}