using System.Text.RegularExpressions;

namespace Backend.Veteriner.Domain.Reports;

/// <summary>
/// Rapor Merkezi'ndeki raporun kararlı anahtarı (favori ve kayıtlı filtre bağı). Katalog panelde tutulur; backend
/// yalnız biçimi doğrular: küçük harf, rakam, <c>-</c> ve <c>.</c> (ör. <c>payments</c>, <c>registers.examinations</c>).
/// </summary>
public static partial class ReportKey
{
    public const int MaxLength = 100;

    public const string InvalidMessage = "Rapor anahtarı 1-100 karakter; küçük harf, rakam, '-' ve '.' içerebilir.";

    public static bool IsValid(string? value)
        => value is { Length: > 0 and <= MaxLength } && Pattern().IsMatch(value);

    /// <summary>Geçersizse <see cref="ArgumentException"/>; geçerliyse değeri döner.</summary>
    public static string Ensure(string? value, string paramName)
        => IsValid(value) ? value! : throw new ArgumentException(InvalidMessage, paramName);

    [GeneratedRegex("^[a-z0-9]+(?:[-.][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
