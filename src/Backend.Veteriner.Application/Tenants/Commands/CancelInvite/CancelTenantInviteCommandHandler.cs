using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.CancelInvite;

/// <summary>
/// Bekleyen daveti iptal eder (Revoked). Idempotent: zaten Revoked ise başarı döner (AlreadyCancelled=true).
/// Accepted davet iptal edilemez. Read-only/cancelled abonelikler için pipeline guard engeller.
/// </summary>
public sealed class CancelTenantInviteCommandHandler
    : IRequestHandler<CancelTenantInviteCommand, Result<CancelTenantInviteResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<TenantInvite> _invitesRead;
    private readonly IRepository<TenantInvite> _invitesWrite;
    private readonly IUnitOfWork _uow;

    public CancelTenantInviteCommandHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<TenantInvite> invitesRead,
        IRepository<TenantInvite> invitesWrite,
        IUnitOfWork uow)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _invitesRead = invitesRead;
        _invitesWrite = invitesWrite;
        _uow = uow;
    }

    public async Task<Result<CancelTenantInviteResultDto>> Handle(CancelTenantInviteCommand request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<CancelTenantInviteResultDto>.Failure(
                "Auth.PermissionDenied",
                "Davet iptali için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<CancelTenantInviteResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<CancelTenantInviteResultDto>.Failure(
                "Tenants.AccessDenied",
                "Davet yalnızca oturumdaki kiracı bağlamında iptal edilebilir.");
        }

        var invite = await _invitesRead.FirstOrDefaultAsync(
            new TenantInviteByTenantAndIdSpec(request.TenantId, request.InviteId), ct);

        if (invite is null)
        {
            return Result<CancelTenantInviteResultDto>.Failure(
                "Invites.NotFound",
                "Davet bulunamadı.");
        }

        if (invite.Status == TenantInviteStatus.Revoked)
        {
            return Result<CancelTenantInviteResultDto>.Success(
                new CancelTenantInviteResultDto(invite.Id, invite.Status, AlreadyCancelled: true));
        }

        if (invite.Status != TenantInviteStatus.Pending)
        {
            return Result<CancelTenantInviteResultDto>.Failure(
                "Invites.InvalidState",
                "Yalnızca bekleyen davet iptal edilebilir; kabul edilmiş davet iptal edilemez.");
        }

        invite.Revoke();
        await _invitesWrite.UpdateAsync(invite, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<CancelTenantInviteResultDto>.Success(
            new CancelTenantInviteResultDto(invite.Id, invite.Status, AlreadyCancelled: false));
    }
}
