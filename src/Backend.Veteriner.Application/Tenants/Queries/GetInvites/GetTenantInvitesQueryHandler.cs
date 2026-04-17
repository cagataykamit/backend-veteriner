using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetInvites;

public sealed class GetTenantInvitesQueryHandler
    : IRequestHandler<GetTenantInvitesQuery, Result<PagedResult<TenantInviteListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<TenantInvite> _invites;
    private readonly IReadRepository<OperationClaim> _claims;
    private readonly IReadRepository<Clinic> _clinics;

    public GetTenantInvitesQueryHandler(
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

    public async Task<Result<PagedResult<TenantInviteListItemDto>>> Handle(
        GetTenantInvitesQuery request,
        CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<PagedResult<TenantInviteListItemDto>>.Failure(
                "Auth.PermissionDenied",
                "Kiracı davet listesi için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<PagedResult<TenantInviteListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<PagedResult<TenantInviteListItemDto>>.Failure(
                "Tenants.AccessDenied",
                "Bu liste yalnızca oturumdaki kiracı bağlamında alınabilir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var searchLower = NormalizeSearchTerm(request.PageRequest.Search);

        var total = await _invites.CountAsync(
            new TenantInvitesCountSpec(request.TenantId, searchLower, request.Status), ct);
        var rows = await _invites.ListAsync(
            new TenantInvitesPagedSpec(request.TenantId, searchLower, request.Status, page, pageSize), ct);

        var utcNow = DateTime.UtcNow;
        var claimIds = rows.Select(r => r.OperationClaimId).Distinct().ToArray();
        var clinicIds = rows.Select(r => r.ClinicId).Distinct().ToArray();

        var claims = claimIds.Length == 0
            ? []
            : await _claims.ListAsync(new OperationClaimsByIdsSpec(claimIds), ct);
        var claimNameById = claims.ToDictionary(c => c.Id, c => c.Name);

        var clinics = clinicIds.Length == 0
            ? []
            : await _clinics.ListAsync(new ClinicsByTenantAndIdsSpec(request.TenantId, clinicIds), ct);
        var clinicNameById = clinics.ToDictionary(c => c.Id, c => c.Name);

        var items = rows.Select(i =>
        {
            var isExpired = i.Status == TenantInviteStatus.Pending && i.ExpiresAtUtc < utcNow;
            claimNameById.TryGetValue(i.OperationClaimId, out var claimName);
            clinicNameById.TryGetValue(i.ClinicId, out var clinicName);
            return new TenantInviteListItemDto(
                i.Id,
                i.Email,
                i.ClinicId,
                clinicName,
                i.OperationClaimId,
                claimName,
                i.Status,
                isExpired,
                i.ExpiresAtUtc,
                i.CreatedAtUtc);
        }).ToList();

        return Result<PagedResult<TenantInviteListItemDto>>.Success(
            PagedResult<TenantInviteListItemDto>.Create(items, total, page, pageSize));
    }

    private static string? NormalizeSearchTerm(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return null;
        return search.Trim().ToLowerInvariant();
    }
}
