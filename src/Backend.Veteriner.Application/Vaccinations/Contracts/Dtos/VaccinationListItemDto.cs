using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;

public sealed record VaccinationListItemDto(
    Guid Id,
    Guid PetId,
    Guid ClinicId,
    Guid? ExaminationId,
    string VaccineName,
    DateTime? AppliedAtUtc,
    DateTime? DueAtUtc,
    VaccinationStatus Status);
