namespace Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;

public sealed record AppointmentReportStatusCountsDto(int Scheduled, int Completed, int Cancelled);

public sealed record AppointmentReportResultDto(
    int TotalCount,
    IReadOnlyList<AppointmentReportItemDto> Items,
    AppointmentReportStatusCountsDto StatusCounts);
