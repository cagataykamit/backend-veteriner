using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Contracts.Dtos;

public sealed record AppointmentCalendarItemDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    Guid ClientId,
    DateTime ScheduledAtUtc,
    AppointmentStatus Status,
    AppointmentType AppointmentType,
    string PetName,
    string ClientName);
