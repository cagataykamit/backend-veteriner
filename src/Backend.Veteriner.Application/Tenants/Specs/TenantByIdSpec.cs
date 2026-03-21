using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class TenantByIdSpec : Specification<Tenant>
{
    public TenantByIdSpec(Guid id)
    {
        Query.Where(t => t.Id == id);
    }
}
