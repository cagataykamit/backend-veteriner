using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Specs;

public sealed class ClientsByTenantIdsSpec : Specification<Client>
{
    public ClientsByTenantIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> clientIds)
    {
        Query.Where(c => c.TenantId == tenantId && clientIds.Contains(c.Id));
    }
}