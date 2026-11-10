using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments.Contracts;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.EDocuments.Commands.Issue;

/// <summary>
/// POST /api/v1/e-documents — seçimden (ücretler / satış / ziyaret) tek belge keser (E_DOCUMENTS_API_CONTRACT §4.2).
/// <c>EDocuments.Issue</c> gerekir. Aynı <see cref="RequestId"/> ilk belgeyi döndürür; seçim tek belge türüne düşmelidir.
/// Belge kaydı ve ücret bağı sağlayıcıya gönderimden önce yazılır; transaction açılmaz (dış çağrı işlem içinde tutulmaz).
/// </summary>
public sealed record IssueEDocumentCommand(
    Guid RequestId,
    Guid? ClinicId,
    IReadOnlyList<Guid>? ChargeIds,
    Guid? SaleId,
    Guid? VisitId,
    string? Note)
    : IRequest<Result<EDocumentDto>>, IAuditableRequest
{
    public string AuditAction => "EDocument.Issue";
    public string? AuditTarget => $"RequestId={RequestId}";
}
