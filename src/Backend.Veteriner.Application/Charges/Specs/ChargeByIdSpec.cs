using Ardalis.Specification;
using Backend.Veteriner.Domain.Charges;

namespace Backend.Veteriner.Application.Charges.Specs;

public sealed class ChargeByIdSpec : Specification<Charge>
{
    public ChargeByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.Id == id);
    }
}
