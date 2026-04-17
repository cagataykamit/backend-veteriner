using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class TenantMembersPagedSpec : Specification<UserTenant>
{
    public Guid TenantIdFilter { get; }

    /// <summary>İşlenmiş arama (küçük harf); null ise e-posta araması yok.</summary>
    public string? SearchTermLower { get; }

    public TenantMembersPagedSpec(Guid tenantId, string? searchTermLower, int page, int pageSize)
    {
        TenantIdFilter = tenantId;
        SearchTermLower = searchTermLower;
        Query.Where(ut => ut.TenantId == tenantId);
        if (!string.IsNullOrEmpty(searchTermLower))
            Query.Where(ut => ut.User != null && ut.User.Email.ToLower().Contains(searchTermLower));

        Query.Include(ut => ut.User!);
        Query.OrderBy(ut => ut.User!.Email).ThenBy(ut => ut.UserId);
        Query.Skip((page - 1) * pageSize).Take(pageSize);
    }
}
