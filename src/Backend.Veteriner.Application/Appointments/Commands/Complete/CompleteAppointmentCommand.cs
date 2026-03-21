using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Commands.Complete;

public sealed record CompleteAppointmentCommand(Guid AppointmentId)
    : IRequest<Result>;
