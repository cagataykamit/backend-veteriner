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
    private readonly IReadRepository<Client> _clients;

    public GetClientsListQueryHandler(IReadRepository<Client> clients) => _clients = clients;

    public async Task<Result<PagedResult<ClientListItemDto>>> Handle(GetClientsListQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var total = await _clients.CountAsync(new ClientsByTenantCountSpec(request.TenantId), ct);
        var rows = await _clients.ListAsync(new ClientsByTenantPagedSpec(request.TenantId, page, pageSize), ct);

        var items = rows
            .Select(c => new ClientListItemDto(c.Id, c.TenantId, c.FullName, c.Phone))
            .ToList();

        return Result<PagedResult<ClientListItemDto>>.Success(
            PagedResult<ClientListItemDto>.Create(items, total, page, pageSize));
    }
}
