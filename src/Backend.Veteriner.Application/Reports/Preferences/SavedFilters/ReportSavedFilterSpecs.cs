using Ardalis.Specification;
using Backend.Veteriner.Domain.Reports;

namespace Backend.Veteriner.Application.Reports.Preferences.SavedFilters;

/// <summary>Kullanıcının bir rapordaki kayıtlı filtreleri, ada göre.</summary>
public sealed class ReportSavedFiltersByReportSpec : Specification<ReportSavedFilter>
{
    public ReportSavedFiltersByReportSpec(Guid tenantId, Guid userId, string reportKey)
    {
        Query.Where(x => x.TenantId == tenantId && x.UserId == userId && x.ReportKey == reportKey)
            .OrderBy(x => x.NormalizedName)
            .AsNoTracking();
    }
}

public sealed class ReportSavedFilterByNameSpec : Specification<ReportSavedFilter>
{
    public ReportSavedFilterByNameSpec(Guid tenantId, Guid userId, string reportKey, string normalizedName)
    {
        Query.Where(x => x.TenantId == tenantId && x.UserId == userId && x.ReportKey == reportKey
                         && x.NormalizedName == normalizedName);
    }
}

/// <summary>Yalnız oturumdaki kullanıcının kaydı; başkasınınki bulunamaz sayılır.</summary>
public sealed class ReportSavedFilterByIdSpec : Specification<ReportSavedFilter>, ISingleResultSpecification<ReportSavedFilter>
{
    public ReportSavedFilterByIdSpec(Guid tenantId, Guid userId, Guid id)
    {
        Query.Where(x => x.TenantId == tenantId && x.UserId == userId && x.Id == id);
    }
}
