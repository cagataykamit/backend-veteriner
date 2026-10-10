using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Visits;
using MediatR;

namespace Backend.Veteriner.Application.Visits.Commands.Correct;

/// <summary>
/// Yanlış geliş veya yanlış durum düzeltmesi (<c>Visits.Correct</c>). Gerekçe zorunludur.
/// <see cref="TargetCareStatus"/> ile <see cref="MarkAsMistaken"/> tam olarak biri verilmelidir.
/// </summary>
public sealed record CorrectVisitCommand(
    Guid VisitId,
    string Reason,
    VisitCareStatus? TargetCareStatus,
    bool MarkAsMistaken)
    : IRequest<Result<VisitDto>>, IAuditableRequest
{
    public string AuditAction => "Visit.Correct";
    public string? AuditTarget => $"VisitId={VisitId}";
}
