using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;

public sealed record AppointmentReportItemDto(
    Guid AppointmentId,
    DateTime ScheduledAtUtc,
    Guid ClinicId,
    string ClinicName,
    Guid ClientId,
    string ClientName,
    Guid PetId,
    string PetName,
    AppointmentStatus Status,
    string? Notes);
