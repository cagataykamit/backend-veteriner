using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Commands.Reschedule;

public sealed record RescheduleAppointmentCommand(
    Guid TenantId,
    Guid AppointmentId,
    DateTime ScheduledAtUtc)
    : IRequest<Result>;
