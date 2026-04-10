using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class OpenBillingCheckoutSessionByTenantSpec : Specification<BillingCheckoutSession>
{
    public OpenBillingCheckoutSessionByTenantSpec(Guid tenantId, DateTime utcNow)
    {
        Query.Where(x =>
                x.TenantId == tenantId
                && (x.Status == BillingCheckoutSessionStatus.Pending || x.Status == BillingCheckoutSessionStatus.RedirectReady)
                && (!x.ExpiresAtUtc.HasValue || x.ExpiresAtUtc > utcNow))
            .OrderByDescending(x => x.CreatedAtUtc);
    }
}

