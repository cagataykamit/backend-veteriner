namespace Backend.Veteriner.Application.Common;

/// <summary>
/// Günlük sahip/hasta araması (SEARCH-001) için tek normalizasyon noktası: Türkçe ı/İ katlama,
/// boşluk/noktalama ile token'lara bölme (sıra bağımsız, her token eşleşmeli) ve telefon/mikroçip biçimi toleransı.
/// Tenant/clinic erişim filtresi ile ilgisi yoktur; yalnızca metin eşleştirme kuralıdır.
/// </summary>
public sealed class DailySearchTerm
{
    public const int MaxTokenCount = 5;

    /// <summary>Kolon tarafında ş/ğ/ü/ö/ç ve harf büyüklüğü farkını yok sayan collation (ı/İ ayrıca katlanır).</summary>
    public const string ColumnCollation = "Latin1_General_100_CI_AI";

    private const string PhoneSeparators = " +()-./";

    private DailySearchTerm(IReadOnlyList<Token> tokens) => Tokens = tokens;

    public IReadOnlyList<Token> Tokens { get; }

    /// <param name="TextPattern">LIKE %token% (ı/İ katlanmış, kaçışlı).</param>
    /// <param name="NumericPattern">Yalnızca rakam token'ı için LIKE %rakamlar% (baştaki 0 atılmış; 905… ile eşleşir); aksi halde null.</param>
    public sealed record Token(string TextPattern, string? NumericPattern);

    /// <summary>Boş/noktalama-only arama için null.</summary>
    public static DailySearchTerm? Create(string? search)
    {
        var normalized = ListQueryTextSearch.Normalize(search);
        if (normalized is null)
            return null;

        var folded = Fold(normalized);

        // "0532 123 45 67", "+90 (532) 123-45-67", "985 1210 0123 4567": tek rakam dizisi.
        if (IsPhoneLike(folded))
            return FromTokens([new string(folded.Where(char.IsDigit).ToArray())]);

        var spaced = new string(folded.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        return FromTokens(spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Kolon tarafındaki <c>Replace</c> ile aynı katlama (ı→i, İ→I).</summary>
    public static string Fold(string value) => value.Replace('ı', 'i').Replace('İ', 'I');

    private static DailySearchTerm? FromTokens(IEnumerable<string> raw)
    {
        var tokens = raw
            .Take(MaxTokenCount)
            .Select(t => new Token(
                ListQueryTextSearch.BuildContainsLikePattern(t),
                BuildNumericPattern(t)))
            .ToList();
        return tokens.Count == 0 ? null : new DailySearchTerm(tokens);
    }

    private static string? BuildNumericPattern(string token)
    {
        if (!token.All(char.IsDigit))
            return null;
        var digits = token.TrimStart('0');
        // Saklanan biçim 905XXXXXXXXX: "0532…" → "532…", "90532…" olduğu gibi alt-dizi olarak eşleşir.
        return digits.Length == 0 ? null : ListQueryTextSearch.BuildContainsLikePattern(digits);
    }

    private static bool IsPhoneLike(string value)
        => value.Any(char.IsDigit) && value.All(c => char.IsDigit(c) || PhoneSeparators.Contains(c));
}
