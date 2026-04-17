using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class TenantInvitesPagedSpec : Specification<TenantInvite>
{
    public Guid TenantIdFilter { get; }

    /// <summary>İşlenmiş arama (küçük harf); null ise e-posta araması yok.</summary>
    public string? SearchTermLower { get; }

    public TenantInviteStatus? StatusFilter { get; }

    public TenantInvitesPagedSpec(Guid tenantId, string? searchTermLower, TenantInviteStatus? status, int page, int pageSize)
    {
        TenantIdFilter = tenantId;
        SearchTermLower = searchTermLower;
        StatusFilter = status;
        Query.Where(i => i.TenantId == tenantId);
        if (!string.IsNullOrEmpty(searchTermLower))
            Query.Where(i => i.Email.ToLower().Contains(searchTermLower));
        if (status.HasValue)
            Query.Where(i => i.Status == status.Value);

        Query.OrderByDescending(i => i.CreatedAtUtc).ThenBy(i => i.Id);
        Query.Skip((page - 1) * pageSize).Take(pageSize);
    }
}
