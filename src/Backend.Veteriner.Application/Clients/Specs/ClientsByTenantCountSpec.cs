using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Specs;

public sealed class ClientsByTenantCountSpec : Specification<Client>
{
    public ClientsByTenantCountSpec(Guid tenantId, DailySearchTerm? searchTerm)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId);
        if (searchTerm is not null)
            Query.WhereDailySearch(searchTerm);
    }
}
