using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Commands.Create;

public sealed record CreateAppointmentCommand(
    Guid TenantId,
    Guid ClinicId,
    Guid PetId,
    DateTime ScheduledAtUtc,
    string? Notes = null)
    : IRequest<Result<Guid>>;
