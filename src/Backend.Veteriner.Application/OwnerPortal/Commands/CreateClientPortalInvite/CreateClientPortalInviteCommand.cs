using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.CreateClientPortalInvite;

/// <summary>
/// Klinik paneli: müşteriyi hasta sahibi portalına davet eder (ADR-031 K3). Davet müşteri kaydındaki e-postaya gider; KVKK Ret
/// veya hatırlatma istemiyor müşteriye gitmez (<c>OwnerPortalInvitePolicy</c>). Klinik context-first; <see cref="ClinicId"/>
/// yalnız bağlam yoksa veya uyuşmazlık denetimi için. Aynı müşterinin bekleyen daveti iptal edilir.
/// </summary>
public sealed record CreateClientPortalInviteCommand(Guid ClientId, Guid? ClinicId)
    : IRequest<Result<ClientPortalInviteDto>>, IAuditableRequest
{
    public string AuditAction => "OwnerPortal.Invite.Create";
    public string? AuditTarget => $"ClientId={ClientId}";
}
