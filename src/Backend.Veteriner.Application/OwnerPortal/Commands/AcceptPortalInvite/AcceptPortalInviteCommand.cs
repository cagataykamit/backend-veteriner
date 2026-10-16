using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.AcceptPortalInvite;

/// <summary>
/// Hasta sahibi daveti kabul eder (ADR-031 K3): hesap yoksa oluşturulur (parola opsiyonel), varsa davet ona bağlanır; bekleyen
/// portal belgeleri kabul edilir (ADR-029), bağ kurulur, davet tüketilir ve oturum açılır. E-posta sahipliğinin kanıtı davet
/// bağlantısıdır (e-postaya gider; v0'da SMS yok). Tek işlem: bir adım başarısızsa hiçbiri kalmaz.
/// </summary>
public sealed record AcceptPortalInviteCommand(
    string Token,
    string? DisplayName,
    string? Password,
    IReadOnlyList<LegalDocumentAcceptanceInput>? AcceptedLegalDocuments)
    : IRequest<Result<PortalInviteAcceptResultDto>>,
      IAuditableRequest,
      ITransactionalRollbackOnFailureRequest,
      IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Portal.Invite.Accept";
    public string? AuditTarget => null;
}
