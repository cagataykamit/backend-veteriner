using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ExaminationTemplates.Commands.Create;

/// <summary>Kliniğe özel şablon ekler (aktif doğar). <see cref="Lines"/> verilmezse satırsız (yalnız metin) şablondur.</summary>
public sealed record CreateExaminationTemplateCommand(
    string Name,
    string? VisitReason,
    string? Findings,
    string? Plan,
    IReadOnlyList<ExaminationTemplateLineRequest>? Lines)
    : IRequest<Result<ExaminationTemplateDto>>, IAuditableRequest
{
    public string AuditAction => "ExaminationTemplate.Create";
    public string? AuditTarget => $"Lines={Lines?.Count ?? 0}";
}
