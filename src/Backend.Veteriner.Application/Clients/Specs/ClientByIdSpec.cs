using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Specs;

public sealed class ClientByIdSpec : Specification<Client>
{
    public ClientByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(c => c.TenantId == tenantId && c.Id == id);
    }
}
