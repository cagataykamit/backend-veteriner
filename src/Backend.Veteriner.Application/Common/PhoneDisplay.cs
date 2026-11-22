using System.Text.RegularExpressions;

namespace Backend.Veteriner.Application.Common;

/// <summary>
/// Telefon numarasının metinlerdeki gösterimi (e-posta şablonları): panel, portal ve klinik sitesiyle aynı biçim. Türkiye numarası
/// "+90 5XX XXX XX XX" olur ("05439580091", "905439580091", "5439580091"; sabit hat ve 0850 dahil); tanınmayan biçim kırpılıp olduğu gibi
/// kalır. Kayıt değişmez, yalnız gösterim.
/// </summary>
public static partial class PhoneDisplay
{
    public static string? Format(string? phone)
    {
        var raw = phone?.Trim();
        if (string.IsNullOrEmpty(raw))
            return raw;

        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        var national = digits.Length == 12 && digits.StartsWith("90", StringComparison.Ordinal) ? digits[2..]
            : digits.Length == 11 && digits[0] == '0' ? digits[1..]
            : digits;

        return TurkishNational().IsMatch(national)
            ? $"+90 {national[..3]} {national[3..6]} {national[6..8]} {national[8..10]}"
            : raw;
    }

    [GeneratedRegex(@"^[2-58]\d{9}$")]
    private static partial Regex TurkishNational();
}
