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
}
