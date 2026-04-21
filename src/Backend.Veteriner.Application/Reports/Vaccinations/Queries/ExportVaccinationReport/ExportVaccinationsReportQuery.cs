using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Vaccinations.Queries.ExportVaccinationReport;

public sealed record ExportVaccinationsReportQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    VaccinationStatus? Status,
    Guid? ClientId,
    Guid? PetId,
    string? Search) : IRequest<Result<VaccinationsCsvExportResult>>;

public sealed record VaccinationsCsvExportResult(byte[] ContentUtf8Bom, string FileDownloadName);
