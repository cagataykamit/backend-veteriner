using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class TenantMembersCountSpec : Specification<UserTenant>
{
    public Guid TenantIdFilter { get; }

    /// <summary>İşlenmiş arama (küçük harf); null ise e-posta araması yok.</summary>
    public string? SearchTermLower { get; }

    public TenantMembersCountSpec(Guid tenantId, string? searchTermLower)
    {
        TenantIdFilter = tenantId;
        SearchTermLower = searchTermLower;
        Query.Where(ut => ut.TenantId == tenantId);
        if (!string.IsNullOrEmpty(searchTermLower))
            Query.Where(ut => ut.User != null && ut.User.Email.ToLower().Contains(searchTermLower));
    }
}
