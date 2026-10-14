using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Specs;

/// <summary>Kiracının peşin müşterisi (en fazla bir kayıt; filtreli benzersiz indeks).</summary>
public sealed class WalkInClientSpec : Specification<Client>
{
    public WalkInClientSpec(Guid tenantId)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.IsWalkIn);
    }
}
