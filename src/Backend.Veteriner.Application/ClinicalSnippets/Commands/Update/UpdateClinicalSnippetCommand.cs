using Backend.Veteriner.Application.ClinicalSnippets.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ClinicalSnippets.Commands.Update;

/// <summary>Kısayol, başlık ve metin düzenlenir; <see cref="RowVersion"/> zorunludur, eski sürümle yazım 409 döner.</summary>
public sealed record UpdateClinicalSnippetCommand(Guid Id, string Shortcut, string Title, string Body, string? RowVersion)
    : IRequest<Result<ClinicalSnippetDto>>, IAuditableRequest
{
    public string AuditAction => "ClinicalSnippet.Update";
    public string? AuditTarget => $"ClinicalSnippetId={Id}";
}
