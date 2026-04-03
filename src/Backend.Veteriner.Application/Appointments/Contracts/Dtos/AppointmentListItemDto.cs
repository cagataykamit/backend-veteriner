using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Contracts.Dtos;

/// <summary>
/// Liste projeksiyonu. <see cref="AppointmentType"/> randevu işlem türü; <see cref="SpeciesName"/> hayvan türüdür.
/// </summary>
public sealed record AppointmentListItemDto(
    Guid Id,
    Guid TenantId,
    Guid ClinicId,
    string ClinicName,
    Guid PetId,
    string PetName,
    Guid SpeciesId,
    string SpeciesName,
    AppointmentType AppointmentType,
    Guid ClientId,
    string ClientName,
    DateTime ScheduledAtUtc,
    AppointmentStatus Status,
    string? Notes);
