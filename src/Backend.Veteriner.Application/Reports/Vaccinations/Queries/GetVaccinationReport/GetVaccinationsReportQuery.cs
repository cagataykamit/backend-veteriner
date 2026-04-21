using Backend.Veteriner.Application.Reports.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Vaccinations.Queries.GetVaccinationReport;

/// <summary>
/// <c>from</c>/<c>to</c> UTC kapalı aralık: önce <c>AppliedAtUtc</c>, yoksa <c>DueAtUtc</c> (liste sıralaması ile aynı eksen).
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
