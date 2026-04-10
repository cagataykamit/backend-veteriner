using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class BillingCheckoutSessionByTenantAndIdSpec : Specification<BillingCheckoutSession>
{
    public BillingCheckoutSessionByTenantAndIdSpec(Guid tenantId, Guid sessionId)
    {
        Query.Where(x => x.TenantId == tenantId && x.Id == sessionId);
    }
}

