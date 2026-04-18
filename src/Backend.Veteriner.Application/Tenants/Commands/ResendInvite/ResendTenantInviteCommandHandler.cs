using System.Security.Cryptography;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.ResendInvite;

/// <summary>
/// Mevcut bekleyen davetin token'ını yeniden üretir ve expiry'yi yeniler (aynı Id korunur).
/// Accepted/Revoked davette çalışmaz. Create akışına dokunmaz; duplicate pending kuralı ilgilendirmez (aynı kayıt üzerinde).
/// Read-only/cancelled abonelikler için pipeline guard engeller.
/// </summary>
public sealed class ResendTenantInviteCommandHandler
    : IRequestHandler<ResendTenantInviteCommand, Result<ResendTenantInviteResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<TenantInvite> _invitesRead;
    private readonly IRepository<TenantInvite> _invitesWrite;
    private readonly ITokenHashService _tokenHash;
    private readonly IUnitOfWork _uow;

    public ResendTenantInviteCommandHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<TenantInvite> invitesRead,
        IRepository<TenantInvite> invitesWrite,
        ITokenHashService tokenHash,
        IUnitOfWork uow)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _invitesRead = invitesRead;
        _invitesWrite = invitesWrite;
        _tokenHash = tokenHash;
        _uow = uow;
    }

    public async Task<Result<ResendTenantInviteResultDto>> Handle(ResendTenantInviteCommand request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<ResendTenantInviteResultDto>.Failure(
                "Auth.PermissionDenied",
                "Davet yeniden gönderimi için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<ResendTenantInviteResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<ResendTenantInviteResultDto>.Failure(
                "Tenants.AccessDenied",
                "Davet yalnızca oturumdaki kiracı bağlamında yeniden gönderilebilir.");
        }

        var invite = await _invitesRead.FirstOrDefaultAsync(
            new TenantInviteByTenantAndIdSpec(request.TenantId, request.InviteId), ct);

        if (invite is null)
        {
            return Result<ResendTenantInviteResultDto>.Failure(
                "Invites.NotFound",
                "Davet bulunamadı.");
        }

        if (invite.Status != TenantInviteStatus.Pending)
        {
            return Result<ResendTenantInviteResultDto>.Failure(
                "Invites.InvalidState",
                "Yalnızca bekleyen davet yeniden gönderilebilir; kabul edilmiş veya iptal edilmiş davet yeniden üretilemez.");
        }

        var utcNow = DateTime.UtcNow;
        var expires = request.ExpiresAtUtc is { } ex
            ? (ex.Kind == DateTimeKind.Utc ? ex : ex.ToUniversalTime())
            : utcNow.AddDays(InviteDefaults.DefaultExpiryDays);

        if (expires <= utcNow)
        {
            return Result<ResendTenantInviteResultDto>.Failure(
                "Invites.ExpiryInvalid",
                "expiresAtUtc gelecekte bir zaman olmalıdır.");
        }

        var rawToken = CreateUrlSafeToken();
        var tokenHash = _tokenHash.ComputeSha256(rawToken);

        invite.Reissue(tokenHash, expires, utcNow);
        await _invitesWrite.UpdateAsync(invite, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<ResendTenantInviteResultDto>.Success(new ResendTenantInviteResultDto(
            invite.Id,
            rawToken,
            invite.Email,
            invite.TenantId,
            invite.ClinicId,
            invite.ExpiresAtUtc));
    }

    private static string CreateUrlSafeToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
