using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Commands.Create;

public sealed record CreateAppointmentCommand(
    Guid? ClinicId,
    Guid PetId,
    DateTime ScheduledAtUtc,
    AppointmentType AppointmentType,
    AppointmentStatus? Status = null,
    string? Notes = null,
    int? DurationMinutes = null)
    : IRequest<Result<Guid>>, IActivationSignalRequest
{
    public ActivationSignalKind ActivationSignal => ActivationSignalKind.AppointmentCreated;
}