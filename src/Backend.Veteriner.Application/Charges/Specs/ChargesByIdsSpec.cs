using Ardalis.Specification;
using Backend.Veteriner.Domain.Charges;

namespace Backend.Veteriner.Application.Charges.Specs;

public sealed class ChargesByIdsSpec : Specification<Charge>
{
    public ChargesByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && ids.Contains(c.Id));
    }
}
