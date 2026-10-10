using Ardalis.Specification;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Specs;

public sealed class ClientsByTenantPagedSpec : Specification<Client, ClientListItemDto>
{
    public ClientsByTenantPagedSpec(Guid tenantId, int page, int pageSize, DailySearchTerm? searchTerm)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId);
        if (searchTerm is not null)
            Query.WhereDailySearch(searchTerm);

        Query
            .OrderBy(c => c.FullName)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        Query.Select(c => new ClientListItemDto(
            c.Id,
            c.TenantId,
            c.CreatedAtUtc,
            c.FullName,
            c.Email,
            c.Phone));
    }
}
