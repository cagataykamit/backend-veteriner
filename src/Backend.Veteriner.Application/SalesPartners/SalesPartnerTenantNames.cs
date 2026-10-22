using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.SalesPartners;

/// <summary>Kod kullanan kiracıların adları (platform yöneticisi raporu; kiracılar arası okuma bilinçli).</summary>
internal static class SalesPartnerTenantNames
{
    public static async Task<Dictionary<Guid, string>> LoadAsync(IReadRepository<Tenant> tenants, IEnumerable<Guid> tenantIds, CancellationToken ct)
    {
        var ids = tenantIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var rows = await tenants.ListAsync(new TenantNamesByIdsSpec(ids), ct);
        return rows.ToDictionary(t => t.Id, t => t.Name);
    }

    private sealed class TenantNamesByIdsSpec : Specification<Tenant>
    {
        public TenantNamesByIdsSpec(IReadOnlyCollection<Guid> ids)
        {
            Query.AsNoTracking();
            Query.Where(t => ids.Contains(t.Id));
        }
    }
}
