using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Examinations.Queries.ExportExaminationReport;

public sealed record ExportExaminationsReportXlsxQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    Guid? ClientId,
    Guid? PetId,
    Guid? AppointmentId,
    string? Search) : IRequest<Result<ExaminationsXlsxExportResult>>;

public sealed record ExaminationsXlsxExportResult(byte[] Content, string FileDownloadName);
