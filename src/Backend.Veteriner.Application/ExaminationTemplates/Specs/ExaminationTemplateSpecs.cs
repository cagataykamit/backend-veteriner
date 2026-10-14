using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ExaminationTemplates.Specs;

/// <summary>
/// Kiracının gördüğü şablon (sistem veya kendi), satırlarıyla. <paramref name="track"/> true ise izlenir: güncellemede satırlar
/// koleksiyon üzerinden değiştirilir ve sürüm kontrolünü yüklenen rowversion yapar.
/// </summary>
public sealed class ExaminationTemplateVisibleByIdSpec : Specification<ExaminationTemplate>
{
    public ExaminationTemplateVisibleByIdSpec(Guid tenantId, Guid id, bool track = false)
    {
        if (!track)
            Query.AsNoTracking();

        Query.Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId))
            .Include(x => x.Lines);
    }
}

/// <summary>Aynı Türkçe ad anahtarı kiracının listesinde (sistem dahil, pasif dahil) var mı.</summary>
public sealed class ExaminationTemplateNameTakenSpec : Specification<ExaminationTemplate>
{
    public ExaminationTemplateNameTakenSpec(Guid tenantId, string normalizedName, Guid? excludeId = null)
    {
        Query.AsNoTracking();
        Query.Where(x => (x.TenantId == null || x.TenantId == tenantId) && x.NormalizedName == normalizedName);
        if (excludeId.HasValue)
            Query.Where(x => x.Id != excludeId.Value);
    }
}

internal static class ExaminationTemplateFilter
{
    public static void Apply(
        ISpecificationBuilder<ExaminationTemplate> query,
        Guid tenantId,
        bool includeInactive,
        IReadOnlyList<string> tokenPatterns)
    {
        query.Where(x => x.TenantId == null || x.TenantId == tenantId);

        if (!includeInactive)
            query.Where(x => x.IsActive);

        foreach (var pattern in tokenPatterns)
            query.Where(x => EF.Functions.Like(x.NormalizedName, pattern));
    }
}

public sealed class ExaminationTemplatesFilteredCountSpec : Specification<ExaminationTemplate>
{
    public ExaminationTemplatesFilteredCountSpec(Guid tenantId, bool includeInactive, IReadOnlyList<string> tokenPatterns)
    {
        ExaminationTemplateFilter.Apply(Query, tenantId, includeInactive, tokenPatterns);
    }
}

/// <summary>Sıra: ada göre. Satırlar dahil (seçici önizlemesi için).</summary>
public sealed class ExaminationTemplatesFilteredPagedSpec : Specification<ExaminationTemplate>
{
    public ExaminationTemplatesFilteredPagedSpec(
        Guid tenantId,
        bool includeInactive,
        IReadOnlyList<string> tokenPatterns,
        int page,
        int pageSize)
    {
        Query.AsNoTracking();
        ExaminationTemplateFilter.Apply(Query, tenantId, includeInactive, tokenPatterns);
        Query.Include(x => x.Lines)
            .OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
