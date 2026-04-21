using Backend.Veteriner.Application.Reports.Examinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Examinations.Queries.GetExaminationReport;

/// <summary><c>from</c>/<c>to</c>: UTC; filtre <c>ExaminedAtUtc</c> üzerinde kapalı aralık <c>[from,to]</c> dahil.</summary>
public sealed record GetExaminationsReportQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    Guid? ClientId,
    Guid? PetId,
    Guid? AppointmentId,
    string? Search,
    int Page,
    int PageSize) : IRequest<Result<ExaminationReportResultDto>>;
