using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Examinations.Queries.ExportExaminationReport;

public sealed record ExportExaminationsReportPdfQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    Guid? ClientId,
    Guid? PetId,
    Guid? AppointmentId,
    string? Search) : IRequest<Result<RegisterExportResult>>;
