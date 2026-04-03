using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

/// <summary>All pets owned by a client within a tenant (id + name projection via entity).</summary>
public sealed class PetsByTenantClientIdSpec : Specification<Pet>
{
    public PetsByTenantClientIdSpec(Guid tenantId, Guid clientId)
    {
        Query.Where(p => p.TenantId == tenantId && p.ClientId == clientId);
    }
}
