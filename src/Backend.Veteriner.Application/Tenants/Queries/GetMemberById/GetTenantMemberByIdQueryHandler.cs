using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Common;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetMemberById;

/// <summary>
/// Tenant paneli için tek üye detayı. Tenant-scoped:
/// <list type="number">
///   <item><c>Tenants.InviteCreate</c> yetkisi zorunlu.</item>
///   <item>JWT <c>tenant_id</c> == route <c>tenantId</c> — aksi halde <c>Tenants.AccessDenied</c>.</item>
///   <item>Üye bu kiracının <c>UserTenant</c> satırında yoksa 404 <c>Members.NotFound</c> (maskeleme).</item>
/// </list>
/// Roles alanı <see cref="InviteAssignableOperationClaimsCatalog"/> whitelist'i ile filtrelenir;
/// Clinics alanı <see cref="IUserClinicRepository.ListAccessibleClinicsAsync"/> üzerinden doldurulur.
/// Global admin yüzeyine (<c>/api/v1/admin/users</c>) düşmez.
/// </summary>
public sealed class GetTenantMemberByIdQueryHandler
    : IRequestHandler<GetTenantMemberByIdQuery, Result<TenantMemberDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<UserTenant> _userTenantsRead;
    private readonly IUserOperationClaimRepository _userOperationClaims;
    private readonly IUserClinicRepository _userClinics;

    public GetTenantMemberByIdQueryHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<UserTenant> userTenantsRead,
        IUserOperationClaimRepository userOperationClaims,
        IUserClinicRepository userClinics)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _userTenantsRead = userTenantsRead;
        _userOperationClaims = userOperationClaims;
        _userClinics = userClinics;
    }

    public async Task<Result<TenantMemberDetailDto>> Handle(GetTenantMemberByIdQuery request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<TenantMemberDetailDto>.Failure(
                "Auth.PermissionDenied",
                "Kiracı üye detayı için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<TenantMemberDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<TenantMemberDetailDto>.Failure(
                "Tenants.AccessDenied",
                "Üye detayı yalnızca oturumdaki kiracı bağlamında alınabilir.");
        }

        var membership = await _userTenantsRead.FirstOrDefaultAsync(
            new UserTenantByMemberSpec(request.TenantId, request.MemberId), ct);

        if (membership?.User is null)
        {
            return Result<TenantMemberDetailDto>.Failure(
                "Members.NotFound",
                "Üye bu kiracıda bulunamadı.");
        }

        var allClaimDetails = await _userOperationClaims.GetDetailsByUserIdAsync(request.MemberId, ct);
        var roles = allClaimDetails
            .Where(d => InviteAssignableOperationClaimsCatalog.IsAssignableName(d.OperationClaimName))
            .Select(d => new TenantMemberRoleDto(d.OperationClaimId, d.OperationClaimName))
            .ToList();

        var clinicEntities = await _userClinics.ListAccessibleClinicsAsync(request.MemberId, request.TenantId, null, ct);
        var clinics = clinicEntities
            .Select(c => new TenantMemberClinicDto(c.Id, c.Name, c.IsActive))
            .ToList();

        var user = membership.User!;
        return Result<TenantMemberDetailDto>.Success(new TenantMemberDetailDto(
            user.Id,
            user.Email,
            TenantMemberDisplayName.DeriveFromEmail(user.Email),
            user.EmailConfirmed,
            membership.CreatedAtUtc,
            roles,
            clinics));
    }
}
