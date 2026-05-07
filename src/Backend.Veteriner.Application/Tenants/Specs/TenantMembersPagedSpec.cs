using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class TenantMembersPagedSpec : Specification<UserTenant, TenantMemberListProjectionRow>
{
    public Guid TenantIdFilter { get; }

    /// <summary>İşlenmiş arama (küçük harf); null ise e-posta araması yok.</summary>
    public string? SearchTermLower { get; }

    public TenantMembersPagedSpec(Guid tenantId, string? searchTermLower, int page, int pageSize)
    {
        TenantIdFilter = tenantId;
        SearchTermLower = searchTermLower;
        Query.AsNoTracking();
        Query.Where(ut => ut.TenantId == tenantId);
        if (!string.IsNullOrEmpty(searchTermLower))
        {
            var pat = ListQueryTextSearch.BuildContainsLikePattern(searchTermLower);
            Query.Where(ut => ut.User != null && EF.Functions.Like(ut.User.Email, pat));
        }

        Query.OrderBy(ut => ut.User!.Email).ThenBy(ut => ut.UserId);
        Query.Skip((page - 1) * pageSize).Take(pageSize);

        Query.Select(ut => new TenantMemberListProjectionRow(
            ut.User!.Id,
            ut.User.Email,
            ut.User.EmailConfirmed,
            ut.User.CreatedAtUtc));
    }
}
