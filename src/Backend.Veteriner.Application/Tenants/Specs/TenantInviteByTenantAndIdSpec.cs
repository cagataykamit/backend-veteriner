using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

/// <summary>
/// Tenant-scoped davet tekil okuma: <c>TenantId</c> + <c>Id</c> eşleşmesi zorunludur.
/// Tenant dışına sızma riskine karşı hiçbir zaman yalnız <c>Id</c> ile sorgulanmamalıdır.
/// </summary>
public sealed class TenantInviteByTenantAndIdSpec : Specification<TenantInvite>
{
    public TenantInviteByTenantAndIdSpec(Guid tenantId, Guid inviteId)
    {
        Query.Where(x => x.TenantId == tenantId && x.Id == inviteId);
    }
}
