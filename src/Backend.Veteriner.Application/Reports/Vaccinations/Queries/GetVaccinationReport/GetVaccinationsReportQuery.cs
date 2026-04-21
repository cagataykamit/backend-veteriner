using Backend.Veteriner.Application.Reports.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Vaccinations.Queries.GetVaccinationReport;

/// <summary>
/// <c>from</c>/<c>to</c> UTC kapalı aralık: Applied → <c>AppliedAtUtc</c>; Scheduled/Cancelled → <c>DueAtUtc</c> (bkz. §31.1).
/// </summary>
public sealed record GetVaccinationsReportQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    VaccinationStatus? Status,
    Guid? ClientId,
    Guid? PetId,
    string? Search,
    int Page,
    int PageSize) : IRequest<Result<VaccinationReportResultDto>>;
