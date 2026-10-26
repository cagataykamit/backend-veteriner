using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Commands.Cancel;

public sealed record CancelAppointmentCommand(Guid AppointmentId, string? Reason = null)
    : IRequest<Result>;
