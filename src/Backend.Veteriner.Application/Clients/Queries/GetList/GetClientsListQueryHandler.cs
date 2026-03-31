using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Queries.GetList;

public sealed class GetClientsListQueryHandler
    : IRequestHandler<GetClientsListQuery, Result<PagedResult<ClientListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Client> _clients;

    public GetClientsListQueryHandler(ITenantContext tenantContext, IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clients = clients;
    }

    public async Task<Result<PagedResult<ClientListItemDto>>> Handle(GetClientsListQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<ClientListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var total = await _clients.CountAsync(new ClientsByTenantCountSpec(tenantId), ct);
        var rows = await _clients.ListAsync(new ClientsByTenantPagedSpec(tenantId, page, pageSize), ct);

        var items = rows
            .Select(c => new ClientListItemDto(c.Id, c.TenantId, c.CreatedAtUtc, c.FullName, c.Email, c.Phone))
            .ToList();

        return Result<PagedResult<ClientListItemDto>>.Success(
            PagedResult<ClientListItemDto>.Create(items, total, page, pageSize));
    }
}
