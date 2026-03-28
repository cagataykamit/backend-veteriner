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
    Guid ClientId,
    Guid SpeciesId,
    /// <summary>Pet türü görünen adı (ör. kedi); JSON: <c>type</c>.</summary>
    string Type,
    DateTime ScheduledAtUtc,
    AppointmentStatus Status,
    string? Notes);