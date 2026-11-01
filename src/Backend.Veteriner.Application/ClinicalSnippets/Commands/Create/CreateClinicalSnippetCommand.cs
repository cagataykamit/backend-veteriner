using Backend.Veteriner.Application.ClinicalSnippets.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ClinicalSnippets.Commands.Create;

/// <summary>Kiracıya metin parçası ekler (aktif doğar). Kısayol baştaki <c>#</c> olmadan da olsa kabul edilir.</summary>
public sealed record CreateClinicalSnippetCommand(string Shortcut, string Title, string Body)
    : IRequest<Result<ClinicalSnippetDto>>, IAuditableRequest
{
    public string AuditAction => "ClinicalSnippet.Create";
    public string? AuditTarget => null;
}
