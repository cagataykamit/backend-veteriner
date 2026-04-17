using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetMembers;

public sealed class GetTenantMembersQueryHandler
    : IRequestHandler<GetTenantMembersQuery, Result<PagedResult<TenantMemberListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<UserTenant> _userTenants;

    public GetTenantMembersQueryHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        IReadRepository<UserTenant> userTenants)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _userTenants = userTenants;
    }

    public async Task<Result<PagedResult<TenantMemberListItemDto>>> Handle(
        GetTenantMembersQuery request,
        CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Tenants.InviteCreate))
        {
            return Result<PagedResult<TenantMemberListItemDto>>.Failure(
                "Auth.PermissionDenied",
                "Kiracı üye listesi için Tenants.InviteCreate yetkisi gerekir.");
        }

        if (_tenantContext.TenantId is not { } jwtTenantId)
        {
            return Result<PagedResult<TenantMemberListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (jwtTenantId != request.TenantId)
        {
            return Result<PagedResult<TenantMemberListItemDto>>.Failure(
                "Tenants.AccessDenied",
                "Bu liste yalnızca oturumdaki kiracı bağlamında alınabilir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var searchLower = NormalizeSearchTerm(request.PageRequest.Search);

        var total = await _userTenants.CountAsync(
            new TenantMembersCountSpec(request.TenantId, searchLower), ct);
        var rows = await _userTenants.ListAsync(
            new TenantMembersPagedSpec(request.TenantId, searchLower, page, pageSize), ct);

        var items = rows
            .Select(ut =>
            {
                var u = ut.User!;
                return new TenantMemberListItemDto(u.Id, u.Email, u.EmailConfirmed, u.CreatedAtUtc);
            })
            .ToList();

        return Result<PagedResult<TenantMemberListItemDto>>.Success(
            PagedResult<TenantMemberListItemDto>.Create(items, total, page, pageSize));
    }

    private static string? NormalizeSearchTerm(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return null;
        return search.Trim().ToLowerInvariant();
    }
}
