using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ExaminationTemplates.Commands.Apply;

/// <summary>
/// Şablonu muayeneye uygular (ADR-020 S4): metinleri birleştirir, satırları muayeneye işlem olarak yazar. Hepsi tek
/// transaction'dadır; herhangi bir satır reddedilirse (stok yetersiz, süresi geçmiş parti vb.) hiçbir şey yazılmaz.
/// <see cref="RowVersion"/> muayenenin sürümüdür (zorunlu); her uygulama sürümü ilerletir, aynı sürümle ikinci uygulama 409 alır.
/// </summary>
public sealed record ApplyExaminationTemplateCommand(Guid ExaminationId, Guid TemplateId, string? RowVersion)
    : IRequest<Result<ExaminationTemplateApplyResultDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "ExaminationTemplate.Apply";
    public string? AuditTarget => $"ExaminationId={ExaminationId};ExaminationTemplateId={TemplateId}";
}
