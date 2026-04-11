using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class BillingCheckoutSessionByIdSpec : Specification<BillingCheckoutSession>
{
    public BillingCheckoutSessionByIdSpec(Guid sessionId)
    {
        Query.Where(x => x.Id == sessionId);
    }
}
