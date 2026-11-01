using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.CreateClientPortalInvite;

/// <summary>
/// Klinik paneli: müşteriyi hasta sahibi portalına davet eder (ADR-031 K3). Davet müşteri kaydındaki e-postaya gider; KVKK Ret
/// müşteriye gitmez; hatırlatma istememek engel değildir (<c>OwnerPortalInvitePolicy</c>, ADR-031 K4 eki). Klinik context-first; <see cref="ClinicId"/>
/// yalnız bağlam yoksa veya uyuşmazlık denetimi için. Aynı müşterinin bekleyen daveti iptal edilir.
/// </summary>
public sealed record CreateClientPortalInviteCommand(Guid ClientId, Guid? ClinicId)
    : IRequest<Result<ClientPortalInviteDto>>, IAuditableRequest
{
    public string AuditAction => "OwnerPortal.Invite.Create";
    public string? AuditTarget => $"ClientId={ClientId}";
}
