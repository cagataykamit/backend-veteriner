using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetList;

public sealed class GetTenantsListQueryHandler
    : IRequestHandler<GetTenantsListQuery, Result<PagedResult<TenantListItemDto>>>
{
    private readonly IReadRepository<Tenant> _tenants;

    public GetTenantsListQueryHandler(IReadRepository<Tenant> tenants) => _tenants = tenants;

    public async Task<Result<PagedResult<TenantListItemDto>>> Handle(GetTenantsListQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var total = await _tenants.CountAsync(new TenantsCountSpec(), ct);
        var rows = await _tenants.ListAsync(new TenantsPagedSpec(page, pageSize), ct);

        var items = rows
            .Select(t => new TenantListItemDto(t.Id, t.Name, t.IsActive, t.CreatedAtUtc))
            .ToList();

        return Result<PagedResult<TenantListItemDto>>.Success(
            PagedResult<TenantListItemDto>.Create(items, total, page, pageSize));
    }
}
