using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Contracts.Dtos;

public sealed record AppointmentCalendarItemDto(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    Guid ClientId,
    DateTime ScheduledAtUtc,
    int DurationMinutes,
    DateTime ScheduledEndUtc,
    AppointmentStatus Status,
    AppointmentType AppointmentType,
    string PetName,
    string ClientName);
