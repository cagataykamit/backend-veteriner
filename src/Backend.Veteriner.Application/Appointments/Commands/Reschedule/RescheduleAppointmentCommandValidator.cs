using FluentValidation;

namespace Backend.Veteriner.Application.Appointments.Commands.Reschedule;

public sealed class RescheduleAppointmentCommandValidator : AbstractValidator<RescheduleAppointmentCommand>
{
    public RescheduleAppointmentCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.ScheduledAtUtc)
            .NotEqual(default(DateTime))
            .WithMessage("Yeni randevu zamanı zorunludur.");
    }
}
