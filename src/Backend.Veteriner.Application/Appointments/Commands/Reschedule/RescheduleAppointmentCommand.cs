using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Commands.Reschedule;

public sealed record RescheduleAppointmentCommand(
    Guid AppointmentId,
    DateTime ScheduledAtUtc)
    : IRequest<Result>;
