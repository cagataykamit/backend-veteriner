using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Organization.Specs;

public sealed class TenantBillingProfileByTenantSpec : Specification<TenantBillingProfile>
{
    public TenantBillingProfileByTenantSpec(Guid tenantId)
        => Query.Where(x => x.TenantId == tenantId);
}
