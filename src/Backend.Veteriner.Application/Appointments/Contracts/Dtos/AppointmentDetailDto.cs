using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Contracts.Dtos;

public sealed record AppointmentDetailDto(
    Guid Id,
    Guid TenantId,
    Guid ClinicId,
    string ClinicName,
    Guid PetId,
    string PetName,
    string ClientName,
    DateTime ScheduledAtUtc,
    AppointmentStatus Status,
    string? Notes);