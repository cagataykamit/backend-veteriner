using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Vaccinations.Queries.ExportVaccinationReport;

public sealed record ExportVaccinationsReportXlsxQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    VaccinationStatus? Status,
    Guid? ClientId,
    Guid? PetId,
    string? Search) : IRequest<Result<VaccinationsXlsxExportResult>>;

public sealed record VaccinationsXlsxExportResult(byte[] Content, string FileDownloadName);
