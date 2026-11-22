using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Common;

/// <summary>Listeleme endpointleri için metin araması: normalizasyon ve SQL LIKE güvenli deseni.</summary>
public static class ListQueryTextSearch
{
    public const int MaxTermLength = 200;

    /// <summary>Trim; boş/whitespace ise null.</summary>
    public static string? Normalize(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return null;
        var t = search.Trim();
        return t.Length > MaxTermLength ? t[..MaxTermLength] : t;
    }

    /// <summary>LIKE içinde % ve _ ve [ için kaçış; başına/sonuna % eklenir.</summary>
    public static string BuildContainsLikePattern(string normalizedTerm)
    {
        var escaped = normalizedTerm
            .Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    /// <summary>
    /// Türkçe anahtarlı arama: metin <see cref="TurkishTextKey.Normalize"/> ile anahtarlanır, boşlukla en çok
    /// <paramref name="maxTokens"/> parçaya bölünür; her parça için LIKE deseni döner (sıra bağımsız eşleşme için).
    /// </summary>
    public static IReadOnlyList<string> BuildKeyTokenPatterns(string? search, int maxTokens)
    {
        var normalized = Normalize(search);
        if (normalized is null)
            return [];

        return TurkishTextKey.Normalize(normalized)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(maxTokens)
            .Select(BuildContainsLikePattern)
            .ToList();
    }
}
