using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ExaminationTemplates.Commands.Update;

/// <summary>
/// Tam güncelleme (PUT): ad, metinler ve satırlar birlikte yazılır; <see cref="Lines"/> zorunludur (boş liste = satırsız).
/// <see cref="RowVersion"/> zorunludur, eski sürümle yazım 409 döner. Aktiflik ayrı uçlarla değişir.
/// </summary>
public sealed record UpdateExaminationTemplateCommand(
    Guid Id,
    string Name,
    string? VisitReason,
    string? Findings,
    string? Plan,
    IReadOnlyList<ExaminationTemplateLineRequest> Lines,
    string? RowVersion)
    : IRequest<Result<ExaminationTemplateDto>>, IAuditableRequest
{
    public string AuditAction => "ExaminationTemplate.Update";
    public string? AuditTarget => $"ExaminationTemplateId={Id}";
}
