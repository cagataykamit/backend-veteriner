using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class TenantsPagedSpec : Specification<Tenant>
{
    public TenantsPagedSpec(int page, int pageSize)
    {
        Query.OrderBy(t => t.Name)
            .ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
