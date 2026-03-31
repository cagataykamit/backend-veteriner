using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Commands.Create;

public sealed record CreateAppointmentCommand(
    Guid? ClinicId,
    Guid PetId,
    DateTime ScheduledAtUtc,
    AppointmentType AppointmentType,
    AppointmentStatus? Status = null,
    string? Notes = null)
    : IRequest<Result<Guid>>;