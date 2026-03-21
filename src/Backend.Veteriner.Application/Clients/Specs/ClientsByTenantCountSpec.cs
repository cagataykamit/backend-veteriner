using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Specs;

public sealed class ClientsByTenantCountSpec : Specification<Client>
{
    public ClientsByTenantCountSpec(Guid tenantId)
    {
        Query.Where(c => c.TenantId == tenantId);
    }
}
