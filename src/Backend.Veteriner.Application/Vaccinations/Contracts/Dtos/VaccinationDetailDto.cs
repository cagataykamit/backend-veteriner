using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;

public sealed record VaccinationDetailDto(
    Guid Id,
    Guid TenantId,
    Guid PetId,
    string PetName,
    string ClientName,
    Guid ClientId,
    Guid ClinicId,
    Guid? ExaminationId,
    string VaccineName,
    DateTime? AppliedAtUtc,
    DateTime? DueAtUtc,
    VaccinationStatus Status,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
