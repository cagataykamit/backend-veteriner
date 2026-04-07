using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class PendingTenantInviteByTenantEmailSpec : Specification<TenantInvite>
{
    public PendingTenantInviteByTenantEmailSpec(Guid tenantId, string emailNormalized, DateTime utcNow)
    {
        Query.Where(x =>
            x.TenantId == tenantId
            && x.Email == emailNormalized
            && x.Status == TenantInviteStatus.Pending
            && x.ExpiresAtUtc > utcNow);
    }
}
