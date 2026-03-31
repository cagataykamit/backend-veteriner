using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Contracts.Dtos;

/// <summary>
/// <see cref="Status"/> yaşam döngüsü; <see cref="AppointmentType"/> randevu türü; <see cref="SpeciesName"/> hayvan türü etiketidir.
/// </summary>
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
    string SpeciesName,
    AppointmentType AppointmentType,
    DateTime ScheduledAtUtc,
    AppointmentStatus Status,
    string? Notes);
