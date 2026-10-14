using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ClinicalTerms.Specs;

/// <summary>Kiracının gördüğü kayıt: sistem varsayılanı veya kendi kaydı. İzlenir (aktif/pasif yazımı için).</summary>
public sealed class ClinicalTermVisibleByIdSpec : Specification<ClinicalTerm>
{
    public ClinicalTermVisibleByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId));
    }
}

/// <summary>
/// Update için izlenmeyen yükleme: kayıt sonra istemcinin beklenen sürümüyle eklenir, böylece UPDATE koşulundaki
/// rowversion istemcinin gönderdiği değerdir.
/// </summary>
public sealed class ClinicalTermForUpdateByIdSpec : Specification<ClinicalTerm>
{
    public ClinicalTermForUpdateByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId));
    }
}

/// <summary>Aynı tür + Türkçe anahtar kiracının listesinde (sistem dahil, pasif dahil) var mı.</summary>
public sealed class ClinicalTermNameTakenSpec : Specification<ClinicalTerm>
{
    public ClinicalTermNameTakenSpec(Guid tenantId, ClinicalTermKind kind, string normalizedName, Guid? excludeId = null)
    {
        Query.AsNoTracking();
        Query.Where(x => (x.TenantId == null || x.TenantId == tenantId)
                         && x.Kind == kind
                         && x.NormalizedName == normalizedName);
        if (excludeId.HasValue)
            Query.Where(x => x.Id != excludeId.Value);
    }
}

/// <summary>
/// Sözlük araması: arama metni <see cref="TurkishTextKey.Normalize"/> ile anahtarlanır ve boşlukla en çok
/// <see cref="MaxTokenCount"/> parçaya bölünür; her parça <c>NormalizedName</c> içinde geçmelidir (sıra bağımsız).
/// </summary>
public static class ClinicalTermSearch
{
    public const int MaxTokenCount = 5;

    public static IReadOnlyList<string> BuildTokenPatterns(string? search)
    {
        var normalized = ListQueryTextSearch.Normalize(search);
        if (normalized is null)
            return [];

        return TurkishTextKey.Normalize(normalized)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(MaxTokenCount)
            .Select(ListQueryTextSearch.BuildContainsLikePattern)
            .ToList();
    }

    internal static void Apply(
        ISpecificationBuilder<ClinicalTerm> query,
        Guid tenantId,
        ClinicalTermKind? kind,
        bool includeInactive,
        IReadOnlyList<string> tokenPatterns)
    {
        query.Where(x => x.TenantId == null || x.TenantId == tenantId);

        if (kind.HasValue)
            query.Where(x => x.Kind == kind.Value);

        if (!includeInactive)
            query.Where(x => x.IsActive);

        foreach (var pattern in tokenPatterns)
            query.Where(x => EF.Functions.Like(x.NormalizedName, pattern));
    }
}

public sealed class ClinicalTermsFilteredCountSpec : Specification<ClinicalTerm>
{
    public ClinicalTermsFilteredCountSpec(
        Guid tenantId,
        ClinicalTermKind? kind,
        bool includeInactive,
        IReadOnlyList<string> tokenPatterns)
    {
        ClinicalTermSearch.Apply(Query, tenantId, kind, includeInactive, tokenPatterns);
    }
}

/// <summary>Sıra: arama varsa adı ilk parçayla başlayanlar önce, sonra ada göre.</summary>
public sealed class ClinicalTermsFilteredPagedSpec : Specification<ClinicalTerm>
{
    public ClinicalTermsFilteredPagedSpec(
        Guid tenantId,
        ClinicalTermKind? kind,
        bool includeInactive,
        IReadOnlyList<string> tokenPatterns,
        int page,
        int pageSize)
    {
        Query.AsNoTracking();
        ClinicalTermSearch.Apply(Query, tenantId, kind, includeInactive, tokenPatterns);

        if (tokenPatterns.Count > 0)
        {
            // "%token%" → "token%": adın başında geçen kayıt seçicide üstte.
            var prefixPattern = tokenPatterns[0][1..];
            Query.OrderByDescending(x => EF.Functions.Like(x.NormalizedName, prefixPattern))
                .ThenBy(x => x.Name);
        }
        else
        {
            Query.OrderBy(x => x.Name);
        }

        Query.Skip((page - 1) * pageSize).Take(pageSize);
    }
}
