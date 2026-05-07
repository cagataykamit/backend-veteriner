using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

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
        Query.AsNoTracking();
        Query.Where(i => i.TenantId == tenantId);
        if (!string.IsNullOrEmpty(searchTermLower))
        {
            var pat = ListQueryTextSearch.BuildContainsLikePattern(searchTermLower);
            Query.Where(i => EF.Functions.Like(i.Email, pat));
        }

        if (status.HasValue)
            Query.Where(i => i.Status == status.Value);
    }
}
