using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ClinicalSnippets.Commands.SetActive;

/// <summary>
/// Parçayı pasifleştirir (seçicide görünmez, yazılmış muayene metinleri etkilenmez) veya yeniden etkinleştirir.
/// Zaten istenen durumdaysa değişiklik yapmadan başarılı döner.
/// </summary>
public sealed record SetClinicalSnippetActiveCommand(Guid Id, bool IsActive) : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => IsActive ? "ClinicalSnippet.Activate" : "ClinicalSnippet.Deactivate";
    public string? AuditTarget => $"ClinicalSnippetId={Id}";
}
