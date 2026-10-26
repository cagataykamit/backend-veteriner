using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Commands.Reschedule;

public sealed record RescheduleAppointmentCommand(
    Guid AppointmentId,
    DateTime ScheduledAtUtc)
    : IRequest<Result>;
