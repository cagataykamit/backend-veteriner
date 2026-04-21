using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Examinations.Queries.ExportExaminationReport;

public sealed record ExportExaminationsReportQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    Guid? ClientId,
    Guid? PetId,
    Guid? AppointmentId,
    string? Search) : IRequest<Result<ExaminationsCsvExportResult>>;

public sealed record ExaminationsCsvExportResult(byte[] ContentUtf8Bom, string FileDownloadName);
