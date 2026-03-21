using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Contracts.Dtos;

public sealed record AppointmentListItemDto(
    Guid Id,
    Guid TenantId,
    Guid ClinicId,
    Guid PetId,
    DateTime ScheduledAtUtc,
    AppointmentStatus Status);
