using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetInviteById;

public sealed class GetTenantInviteByIdQueryHandler
    : IRequestHandler<GetTenantInviteByIdQuery, Result<TenantInviteDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<TenantInvite> _invites;
    private readonly IReadRepository<OperationClaim> _claims;
    private readonly IReadRepository<Clinic> _clinics;

    public GetTenantInviteByIdQueryHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<TenantInvite> invites,
        IReadRepository<OperationClaim> claims,
        IReadRepository<Clinic> clinics)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _invites = invites;
        _claims = claims;
        _clinics = clinics;
    }

    public async Task<Result<TenantInviteDetailDto>> Handle(GetTenantInviteByIdQuery request, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<TenantInviteDetailDto>.Failure(
                "Auth.PermissionDenied",
                "Davet detayı için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<TenantInviteDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<TenantInviteDetailDto>.Failure(
                "Tenants.AccessDenied",
                "Davet yalnızca oturumdaki kiracı bağlamında görüntülenebilir.");
        }

        var invite = await _invites.FirstOrDefaultAsync(
            new TenantInviteByTenantAndIdSpec(request.TenantId, request.InviteId), ct);

        if (invite is null)
        {
            return Result<TenantInviteDetailDto>.Failure(
                "Invites.NotFound",
                "Davet bulunamadı.");
        }

        var claim = await _claims.FirstOrDefaultAsync(new OperationClaimByIdSpec(invite.OperationClaimId), ct);
        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(invite.TenantId, invite.ClinicId), ct);

        var utcNow = DateTime.UtcNow;
        var isExpired = invite.Status == TenantInviteStatus.Pending && invite.ExpiresAtUtc < utcNow;
        var canLifecycle = invite.Status == TenantInviteStatus.Pending;

        return Result<TenantInviteDetailDto>.Success(new TenantInviteDetailDto(
            invite.Id,
            invite.TenantId,
            invite.Email,
            invite.ClinicId,
            clinic?.Name,
            invite.OperationClaimId,
            claim?.Name,
            invite.Status,
            isExpired,
            invite.ExpiresAtUtc,
            invite.CreatedAtUtc,
            invite.AcceptedAtUtc,
            invite.AcceptedByUserId,
            canLifecycle,
            canLifecycle));
    }
}
