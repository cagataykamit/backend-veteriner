using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ExaminationTemplates.Commands.SetActive;

/// <summary>
/// Kliniğe özel şablonu pasifleştirir (seçicide görünmez, uygulanamaz; daha önce uygulandığı muayeneler etkilenmez) veya
/// yeniden etkinleştirir. Zaten istenen durumdaysa değişiklik yapmadan başarılı döner.
/// </summary>
public sealed record SetExaminationTemplateActiveCommand(Guid Id, bool IsActive) : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => IsActive ? "ExaminationTemplate.Activate" : "ExaminationTemplate.Deactivate";
    public string? AuditTarget => $"ExaminationTemplateId={Id}";
}
