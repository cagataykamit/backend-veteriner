namespace Backend.Veteriner.Application.Reports.Vaccinations.Contracts.Dtos;

public sealed record VaccinationReportResultDto(
    int TotalCount,
    IReadOnlyList<VaccinationReportItemDto> Items);
