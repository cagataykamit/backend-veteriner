using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class PendingTenantInvitesByTenantCountSpec : Specification<TenantInvite>
{
    public PendingTenantInvitesByTenantCountSpec(Guid tenantId, DateTime utcNow)
    {
        Query.Where(x =>
            x.TenantId == tenantId
            && x.Status == TenantInviteStatus.Pending
            && x.ExpiresAtUtc > utcNow);
    }
}
