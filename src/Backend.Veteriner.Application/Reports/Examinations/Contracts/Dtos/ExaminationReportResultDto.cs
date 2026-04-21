namespace Backend.Veteriner.Application.Reports.Examinations.Contracts.Dtos;

public sealed record ExaminationReportResultDto(
    int TotalCount,
    IReadOnlyList<ExaminationReportItemDto> Items);
