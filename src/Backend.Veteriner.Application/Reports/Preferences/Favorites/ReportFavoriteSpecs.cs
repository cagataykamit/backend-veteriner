using Ardalis.Specification;
using Backend.Veteriner.Domain.Reports;

namespace Backend.Veteriner.Application.Reports.Preferences.Favorites;

/// <summary>Kullanıcının favorileri, eklenme sırasıyla.</summary>
public sealed class ReportFavoritesByUserSpec : Specification<ReportFavorite>
{
    public ReportFavoritesByUserSpec(Guid tenantId, Guid userId)
    {
        Query.Where(x => x.TenantId == tenantId && x.UserId == userId)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.ReportKey)
            .AsNoTracking();
    }
}

public sealed class ReportFavoriteByKeySpec : Specification<ReportFavorite>, ISingleResultSpecification<ReportFavorite>
{
    public ReportFavoriteByKeySpec(Guid tenantId, Guid userId, string reportKey)
    {
        Query.Where(x => x.TenantId == tenantId && x.UserId == userId && x.ReportKey == reportKey);
    }
}
