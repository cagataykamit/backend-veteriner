using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Specs;

public sealed class ClientsByTenantPagedSpec : Specification<Client>
{
    public ClientsByTenantPagedSpec(Guid tenantId, int page, int pageSize)
    {
        Query.Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.FullName)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
