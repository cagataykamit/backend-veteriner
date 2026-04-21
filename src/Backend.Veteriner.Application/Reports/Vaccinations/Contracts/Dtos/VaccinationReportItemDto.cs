using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Reports.Vaccinations.Contracts.Dtos;

/// <param name="NextDueAtUtc">Domain <see cref="Domain.Vaccinations.Vaccination.DueAtUtc"/>; JSON <c>nextDueAtUtc</c>.</param>
/// <param name="EffectiveReportDateUtc"><c>from</c>/<c>to</c> ile uyumlu tek rapor tarihi (UTC); JSON <c>effectiveReportDateUtc</c>.</param>
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
    DateTime? NextDueAtUtc,
    DateTime? EffectiveReportDateUtc,
    string? Notes);
