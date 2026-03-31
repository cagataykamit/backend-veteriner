using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Commands.Update;

public sealed record UpdateAppointmentCommand(
    Guid Id,
    Guid? ClinicId,
    Guid PetId,
    DateTime ScheduledAtUtc,
    AppointmentType AppointmentType,
    AppointmentStatus Status,
    string? Notes = null) : IRequest<Result>;