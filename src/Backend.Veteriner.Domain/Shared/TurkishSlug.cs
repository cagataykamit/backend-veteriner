using System.Globalization;
using System.Text;

namespace Backend.Veteriner.Domain.Shared;

/// <summary>
/// Türkçe metnin adres parçası biçimi — tek yer: "Kadıköy" → "kadikoy", "İstanbul" → "istanbul", "Çağrı Veteriner" →
/// "cagri-veteriner". Türkçe harfler <see cref="TurkishTextKey"/> ile dönüşür (I/ı/İ/i hepsi "i"), kalan aksanlar tabanına iner,
/// harf ya da rakam olmayan her şey tek tireye düşer; başta ve sonda tire kalmaz. Uzunluk sınırı çağıranın kuralıdır.
/// </summary>
public static class TurkishSlug
{
    public static string From(string? value)
    {
        var result = new StringBuilder(value?.Length ?? 0);
        foreach (var ch in TurkishTextKey.Normalize(value).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
                result.Append(ch);
            else if (result.Length > 0 && result[^1] != '-')
                result.Append('-');
        }

        return result.ToString().TrimEnd('-');
    }
}
