using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class TenantInvitesCountSpec : Specification<TenantInvite>
{
    public Guid TenantIdFilter { get; }

    /// <summary>İşlenmiş arama (küçük harf); null ise e-posta araması yok.</summary>
    public string? SearchTermLower { get; }

    public TenantInviteStatus? StatusFilter { get; }

    public TenantInvitesCountSpec(Guid tenantId, string? searchTermLower, TenantInviteStatus? status)
    {
        TenantIdFilter = tenantId;
        SearchTermLower = searchTermLower;
        StatusFilter = status;
        Query.Where(i => i.TenantId == tenantId);
        if (!string.IsNullOrEmpty(searchTermLower))
            Query.Where(i => i.Email.ToLower().Contains(searchTermLower));
        if (status.HasValue)
            Query.Where(i => i.Status == status.Value);
    }
}
