using System.Text.RegularExpressions;

namespace Backend.Veteriner.Application.Ai;

/// <summary>
/// Modele giden serbest metinden kimlik bilgisini çıkarır (ADR-044 KVKK çalışma varsayımı: mümkün olduğunca kimlik bilgisi
/// gönderilmez). Müşteri kaydı hiç gönderilmez; bu sınıf yalnız klinik notlarına yazılmış olabilecekleri temizler: e-posta,
/// telefon / TCKN / mikroçip gibi uzun numaralar (tarih biçimi hariç) ve verilen adlar (ör. hayvan sahibinin ad soyadı).
/// Ad eşlemesi tam kelime ve büyük/küçük harf duyarsızdır; Türkçe İ/ı farkı ve yazım hataları yakalanmayabilir.
/// </summary>
public static partial class AiTextRedactor
{
    public const string EmailMask = "[e-posta]";
    public const string NumberMask = "[numara]";
    public const string NameMask = "[kişi]";

    /// <summary>En az bu kadar rakam taşıyan sayı dizisi numara sayılır (telefon 10–12, TCKN 11, mikroçip 15).</summary>
    private const int MinDigits = 10;

    public static string? Redact(string? text, IReadOnlyCollection<string> names)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var result = EmailPattern().Replace(text, EmailMask);
        result = NumberPattern().Replace(result, m => LooksLikeIdentifier(m.Value) ? NumberMask : m.Value);
        foreach (var name in names)
        {
            result = Regex.Replace(
                result,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])",
                NameMask,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(200));
        }

        return result;
    }

    /// <summary>Ad soyad metnini ayrı kelimelere böler (3 harften kısa parçalar alınmaz: "Ak" gibi kelimeler notta sık geçer).</summary>
    public static IReadOnlyList<string> NameTerms(string? fullName)
        => string.IsNullOrWhiteSpace(fullName)
            ? []
            : fullName.Split([' ', '\t', '-', '.', ','], StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p.Length >= 3)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    // Tarih + saat ("12.03.2026 14") numara değildir; tarih çıkarıldıktan sonra kalan rakamlar sayılır.
    private static bool LooksLikeIdentifier(string value)
        => DatePattern().Replace(value, string.Empty).Count(char.IsDigit) >= MinDigits;

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    // Rakamla başlayıp biten, aralarında boşluk, nokta, tire, parantez olabilen dizi; "+90" dahil.
    [GeneratedRegex(@"(?<![\p{L}\p{N}])\+?\d[\d \-.()/]{6,}\d(?![\p{L}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    // GG.AA.YYYY / GG/AA/YY (geçerli gün ve ay) ya da YYYY-AA-GG; "123-45-67" gibi telefon parçaları tarih sayılmaz.
    [GeneratedRegex(@"(?<!\d)(0?[1-9]|[12]\d|3[01])[./](0?[1-9]|1[0-2])[./](\d{4}|\d{2})(?!\d)|(?<!\d)\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();
}
