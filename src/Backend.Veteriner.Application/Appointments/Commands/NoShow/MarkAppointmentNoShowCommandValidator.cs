using Backend.Veteriner.Domain.Appointments;
using FluentValidation;

namespace Backend.Veteriner.Application.Appointments.Commands.NoShow;

public sealed class MarkAppointmentNoShowCommandValidator : AbstractValidator<MarkAppointmentNoShowCommand>
{
    public MarkAppointmentNoShowCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.Reason)
            .MaximumLength(Appointment.MaxNoShowReasonLength)
            .When(x => !string.IsNullOrEmpty(x.Reason));
    }
}
