using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Vaccinations.Queries.ExportVaccinationReport;

public sealed record ExportVaccinationsReportPdfQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    VaccinationStatus? Status,
    Guid? ClientId,
    Guid? PetId,
    string? Search) : IRequest<Result<RegisterExportResult>>;
