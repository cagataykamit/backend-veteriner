using Ardalis.Specification;
using Backend.Veteriner.Domain.Charges;

namespace Backend.Veteriner.Application.Charges.Specs;

public sealed class ChargeByRequestIdSpec : Specification<Charge>
{
    public ChargeByRequestIdSpec(Guid tenantId, Guid requestId)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.RequestId == requestId);
    }
}
