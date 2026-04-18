using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.AssignMemberRole;

/// <summary>
/// Tenant-scoped rol atama. Global admin <c>/api/v1/admin/users/{userId}/operation-claims/{claimId}</c>
/// yüzeyine düşmez. Güvenlik katmanları:
/// <list type="number">
///   <item><c>Tenants.InviteCreate</c> yetkisi zorunlu.</item>
///   <item>JWT <c>tenant_id</c> == route <c>tenantId</c> — aksi halde <c>Tenants.AccessDenied</c>.</item>
///   <item>Üye bu kiracının <c>UserTenant</c> satırında yoksa 404 <c>Members.NotFound</c> (maskeleme).</item>
///   <item>Claim bulunamazsa <c>Invites.OperationClaimNotFound</c>.</item>
///   <item>Whitelist dışı claim için <c>Invites.OperationClaimNotAssignable</c> (teknik/internal claim'ler tenant panelinden atanamaz).</item>
/// </list>
/// Idempotent: ilişki zaten varsa kayıt eklenmez, cache düşürülmez, <c>AlreadyAssigned = true</c> döner.
/// Read-only / cancelled tenant durumunda merkezi <c>TenantSubscriptionWriteGuardBehavior</c> bu command'ı önce keser.
/// </summary>
public sealed class AssignTenantMemberRoleCommandHandler
    : IRequestHandler<AssignTenantMemberRoleCommand, Result<AssignTenantMemberRoleResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IUserTenantRepository _userTenantRepo;
    private readonly IReadRepository<OperationClaim> _claimsRead;
    private readonly IUserOperationClaimRepository _userOperationClaims;
    private readonly IPermissionCacheInvalidator _cache;
    private readonly IUnitOfWork _uow;

    public AssignTenantMemberRoleCommandHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IUserTenantRepository userTenantRepo,
        IReadRepository<OperationClaim> claimsRead,
        IUserOperationClaimRepository userOperationClaims,
        IPermissionCacheInvalidator cache,
        IUnitOfWork uow)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _userTenantRepo = userTenantRepo;
        _claimsRead = claimsRead;
        _userOperationClaims = userOperationClaims;
        _cache = cache;
        _uow = uow;
    }

    public async Task<Result<AssignTenantMemberRoleResultDto>> Handle(AssignTenantMemberRoleCommand request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<AssignTenantMemberRoleResultDto>.Failure(
                "Auth.PermissionDenied",
                "Kiracı üyesine rol atamak için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<AssignTenantMemberRoleResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<AssignTenantMemberRoleResultDto>.Failure(
                "Tenants.AccessDenied",
                "Rol ataması yalnızca oturumdaki kiracı bağlamında yapılabilir.");
        }

        if (!await _userTenantRepo.ExistsAsync(request.MemberId, request.TenantId, ct))
        {
            return Result<AssignTenantMemberRoleResultDto>.Failure(
                "Members.NotFound",
                "Üye bu kiracıda bulunamadı.");
        }

        var claim = await _claimsRead.FirstOrDefaultAsync(new OperationClaimByIdSpec(request.OperationClaimId), ct);
        if (claim is null)
        {
            return Result<AssignTenantMemberRoleResultDto>.Failure(
                "Invites.OperationClaimNotFound",
                "Geçersiz operationClaimId.");
        }

        if (!InviteAssignableOperationClaimsCatalog.IsAssignableName(claim.Name))
        {
            return Result<AssignTenantMemberRoleResultDto>.Failure(
                "Invites.OperationClaimNotAssignable",
                "Bu operation claim tenant panelinden atanamaz; yalnızca atanabilir rol listesindekiler seçilebilir.");
        }

        if (await _userOperationClaims.ExistsAsync(request.MemberId, request.OperationClaimId, ct))
        {
            return Result<AssignTenantMemberRoleResultDto>.Success(new AssignTenantMemberRoleResultDto(
                request.MemberId,
                claim.Id,
                claim.Name,
                AlreadyAssigned: true));
        }

        var entity = new UserOperationClaim(request.MemberId, request.OperationClaimId);
        await _userOperationClaims.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        _cache.InvalidateUser(request.MemberId);

        return Result<AssignTenantMemberRoleResultDto>.Success(new AssignTenantMemberRoleResultDto(
            request.MemberId,
            claim.Id,
            claim.Name,
            AlreadyAssigned: false));
    }
}
