using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ClinicalTerms.Commands.SetActive;

/// <summary>
/// Kliniğe özel kaydı pasifleştirir (seçicide görünmez, geçmiş muayene metinleri etkilenmez) veya yeniden etkinleştirir.
/// Zaten istenen durumdaysa değişiklik yapmadan başarılı döner.
/// </summary>
public sealed record SetClinicalTermActiveCommand(Guid Id, bool IsActive) : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => IsActive ? "ClinicalTerm.Activate" : "ClinicalTerm.Deactivate";
    public string? AuditTarget => $"ClinicalTermId={Id}";
}
