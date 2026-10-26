using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.LegalAcceptances.Commands.AcceptMy;

/// <summary>
/// Oturumdaki kullanıcı bekleyen belgelerin güncel sürümünü kabul eder (ADR-029 madde 5). Her bekleyen belge için yeni
/// kabul satırı yazılır; zaten güncel olan veya kullanıcıya gerekmeyen belgeler yok sayılır. Abonelik salt okunurken de
/// çalışır (kabul edilmeden panele girilemez).
/// </summary>
public sealed record AcceptMyLegalDocumentsCommand(IReadOnlyList<LegalDocumentAcceptanceInput>? Documents)
    : IRequest<Result<MyLegalAcceptanceStatusDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Account.LegalAcceptance";
    public string? AuditTarget => "SelfService";
}
