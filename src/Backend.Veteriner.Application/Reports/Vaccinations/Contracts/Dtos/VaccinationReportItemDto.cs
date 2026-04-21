using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Reports.Vaccinations.Contracts.Dtos;

public sealed record VaccinationReportItemDto(
    Guid VaccinationId,
    Guid ClinicId,
    string ClinicName,
    Guid ClientId,
    string ClientName,
    Guid PetId,
    string PetName,
    string VaccineName,
    VaccinationStatus Status,
    DateTime? AppliedAtUtc,
    DateTime? DueAtUtc,
    string? Notes);
