using Ardalis.Specification;
using Backend.Veteriner.Application.ClinicalTerms.Specs;
using Backend.Veteriner.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ClinicalSnippets.Specs;

/// <summary>Kiracının kaydı; izlenir (aktif/pasif yazımı için).</summary>
public sealed class ClinicalSnippetByIdSpec : Specification<ClinicalSnippet>
{
    public ClinicalSnippetByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(x => x.Id == id && x.TenantId == tenantId);
    }
}

/// <summary>
/// Update için izlenmeyen yükleme: kayıt sonra istemcinin beklenen sürümüyle eklenir, böylece UPDATE koşulundaki
/// rowversion istemcinin gönderdiği değerdir.
/// </summary>
public sealed class ClinicalSnippetForUpdateByIdSpec : Specification<ClinicalSnippet>
{
    public ClinicalSnippetForUpdateByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(x => x.Id == id && x.TenantId == tenantId);
    }
}

/// <summary>Aynı kısayol kiracıda (pasif dahil) var mı.</summary>
public sealed class ClinicalSnippetShortcutTakenSpec : Specification<ClinicalSnippet>
{
    public ClinicalSnippetShortcutTakenSpec(Guid tenantId, string shortcut, Guid? excludeId = null)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.Shortcut == shortcut);
        if (excludeId.HasValue)
            Query.Where(x => x.Id != excludeId.Value);
    }
}

/// <summary>Kiracının tüm parçaları (tavan sayımı için).</summary>
public sealed class ClinicalSnippetsByTenantCountSpec : Specification<ClinicalSnippet>
{
    public ClinicalSnippetsByTenantCountSpec(Guid tenantId)
    {
        Query.Where(x => x.TenantId == tenantId);
    }
}

/// <summary>
/// Liste araması: her arama parçası kısayolda veya başlıkta geçmeli (Türkçe duyarsız; <see cref="ClinicalTermSearch"/> ile aynı anahtar).
/// Gövdede arama yapılmaz.
/// </summary>
internal static class ClinicalSnippetSearch
{
    public static void Apply(
        ISpecificationBuilder<ClinicalSnippet> query,
        Guid tenantId,
        bool includeInactive,
        IReadOnlyList<string> tokenPatterns)
    {
        query.Where(x => x.TenantId == tenantId);

        if (!includeInactive)
            query.Where(x => x.IsActive);

        foreach (var pattern in tokenPatterns)
            query.Where(x => EF.Functions.Like(x.Shortcut, pattern) || EF.Functions.Like(x.NormalizedTitle, pattern));
    }
}

public sealed class ClinicalSnippetsFilteredCountSpec : Specification<ClinicalSnippet>
{
    public ClinicalSnippetsFilteredCountSpec(Guid tenantId, bool includeInactive, IReadOnlyList<string> tokenPatterns)
    {
        ClinicalSnippetSearch.Apply(Query, tenantId, includeInactive, tokenPatterns);
    }
}

/// <summary>Sıra: arama varsa kısayolu ilk parçayla başlayanlar önce, sonra kısayola göre.</summary>
public sealed class ClinicalSnippetsFilteredPagedSpec : Specification<ClinicalSnippet>
{
    public ClinicalSnippetsFilteredPagedSpec(
        Guid tenantId,
        bool includeInactive,
        IReadOnlyList<string> tokenPatterns,
        int page,
        int pageSize)
    {
        Query.AsNoTracking();
        ClinicalSnippetSearch.Apply(Query, tenantId, includeInactive, tokenPatterns);

        if (tokenPatterns.Count > 0)
        {
            // "%token%" → "token%": kısayolu parçayla başlayan kayıt seçicide üstte.
            var prefixPattern = tokenPatterns[0][1..];
            Query.OrderByDescending(x => EF.Functions.Like(x.Shortcut, prefixPattern))
                .ThenBy(x => x.Shortcut);
        }
        else
        {
            Query.OrderBy(x => x.Shortcut);
        }

        Query.Skip((page - 1) * pageSize).Take(pageSize);
    }
}
