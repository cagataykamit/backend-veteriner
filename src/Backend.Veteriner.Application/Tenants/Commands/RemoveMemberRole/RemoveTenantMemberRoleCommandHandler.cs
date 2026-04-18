using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.RemoveMemberRole;

/// <summary>
/// Tenant-scoped rol kaldırma. Global admin yüzeyine düşmez. Güvenlik katmanları:
/// <list type="number">
///   <item><c>Tenants.InviteCreate</c> yetkisi zorunlu.</item>
///   <item>JWT <c>tenant_id</c> == route <c>tenantId</c> — aksi halde <c>Tenants.AccessDenied</c>.</item>
///   <item>Çağıran kendi üzerinden rol çıkaramaz — <c>Invites.SelfRoleRemoveForbidden</c> (kilitlenme koruması).</item>
///   <item>Üye bu kiracının <c>UserTenant</c> satırında yoksa 404 <c>Members.NotFound</c>.</item>
///   <item>Claim bulunamazsa <c>Invites.OperationClaimNotFound</c>.</item>
///   <item>Whitelist dışı claim için <c>Invites.OperationClaimNotAssignable</c> (aynı whitelist kuralı; tenant yüzeyi teknik rolleri görmemeli/yönetmemeli).</item>
/// </list>
/// Idempotent: ilişki yoksa remove çağrılmaz, cache düşürülmez, <c>AlreadyRemoved = true</c> döner.
/// Read-only / cancelled tenant durumunda merkezi <c>TenantSubscriptionWriteGuardBehavior</c> bu command'ı önce keser.
/// </summary>
public sealed class RemoveTenantMemberRoleCommandHandler
    : IRequestHandler<RemoveTenantMemberRoleCommand, Result<RemoveTenantMemberRoleResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IUserTenantRepository _userTenantRepo;
    private readonly IReadRepository<OperationClaim> _claimsRead;
    private readonly IUserOperationClaimRepository _userOperationClaims;
    private readonly IPermissionCacheInvalidator _cache;
    private readonly IUnitOfWork _uow;

    public RemoveTenantMemberRoleCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        ICurrentUserPermissionChecker permissions,
        IUserTenantRepository userTenantRepo,
        IReadRepository<OperationClaim> claimsRead,
        IUserOperationClaimRepository userOperationClaims,
        IPermissionCacheInvalidator cache,
        IUnitOfWork uow)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _permissions = permissions;
        _userTenantRepo = userTenantRepo;
        _claimsRead = claimsRead;
        _userOperationClaims = userOperationClaims;
        _cache = cache;
        _uow = uow;
    }

    public async Task<Result<RemoveTenantMemberRoleResultDto>> Handle(RemoveTenantMemberRoleCommand request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<RemoveTenantMemberRoleResultDto>.Failure(
                "Auth.PermissionDenied",
                "Kiracı üyesinden rol çıkarmak için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<RemoveTenantMemberRoleResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<RemoveTenantMemberRoleResultDto>.Failure(
                "Tenants.AccessDenied",
                "Rol çıkarma yalnızca oturumdaki kiracı bağlamında yapılabilir.");
        }

        if (_clientContext.UserId is { } callerId && callerId == request.MemberId)
        {
            return Result<RemoveTenantMemberRoleResultDto>.Failure(
                "Invites.SelfRoleRemoveForbidden",
                "Kullanıcı kendi üzerinden rol kaldıramaz; kilitlenme riskini önlemek için başka bir yönetici işlemi yapmalıdır.");
        }

        if (!await _userTenantRepo.ExistsAsync(request.MemberId, request.TenantId, ct))
        {
            return Result<RemoveTenantMemberRoleResultDto>.Failure(
                "Members.NotFound",
                "Üye bu kiracıda bulunamadı.");
        }

        var claim = await _claimsRead.FirstOrDefaultAsync(new OperationClaimByIdSpec(request.OperationClaimId), ct);
        if (claim is null)
        {
            return Result<RemoveTenantMemberRoleResultDto>.Failure(
                "Invites.OperationClaimNotFound",
                "Geçersiz operationClaimId.");
        }

        if (!InviteAssignableOperationClaimsCatalog.IsAssignableName(claim.Name))
        {
            return Result<RemoveTenantMemberRoleResultDto>.Failure(
                "Invites.OperationClaimNotAssignable",
                "Bu operation claim tenant panelinden yönetilemez; teknik/internal roller yalnız global admin yüzeyinden yönetilir.");
        }

        if (!await _userOperationClaims.ExistsAsync(request.MemberId, request.OperationClaimId, ct))
        {
            return Result<RemoveTenantMemberRoleResultDto>.Success(new RemoveTenantMemberRoleResultDto(
                request.MemberId,
                claim.Id,
                AlreadyRemoved: true));
        }

        await _userOperationClaims.RemoveAsync(request.MemberId, request.OperationClaimId, ct);
        await _uow.SaveChangesAsync(ct);

        _cache.InvalidateUser(request.MemberId);

        return Result<RemoveTenantMemberRoleResultDto>.Success(new RemoveTenantMemberRoleResultDto(
            request.MemberId,
            claim.Id,
            AlreadyRemoved: false));
    }
}
