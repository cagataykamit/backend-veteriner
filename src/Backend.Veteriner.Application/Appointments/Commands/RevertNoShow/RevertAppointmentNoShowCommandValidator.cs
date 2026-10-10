using FluentValidation;

namespace Backend.Veteriner.Application.Appointments.Commands.RevertNoShow;

public sealed class RevertAppointmentNoShowCommandValidator : AbstractValidator<RevertAppointmentNoShowCommand>
{
    public RevertAppointmentNoShowCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}
