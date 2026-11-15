using System.Text.RegularExpressions;

namespace Backend.Veteriner.Domain.Clients;

/// <summary>
/// Türkiye cep telefonu: saklama ve mükerrer kontrol için tek standart <c>905XXXXXXXXX</c> (12 rakam, <c>^905\d{9}$</c>).
/// </summary>
public static class TurkishMobilePhone
{
    private static readonly Regex Valid12 = new(@"^905\d{9}$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Boş veya yalnızca boşluk: başarı, <paramref name="normalized12"/> null (telefon yok).
    /// Geçerli Türkiye cep girişi: <paramref name="normalized12"/> uluslararası 12 hane.
    /// </summary>
    public static bool TryNormalize(string? input, out string? normalized12)
    {
        normalized12 = null;
        if (string.IsNullOrWhiteSpace(input))
            return true;

        var digits = ExtractDigits(input);
        if (digits.Length == 0)
            return false;
        if (digits.Length > 12)
            return false;

        string? candidate = null;

        if (digits.Length == 12 && digits.StartsWith("90", StringComparison.Ordinal))
            candidate = digits;
        else if (digits.Length == 11 && digits[0] == '0' && digits[1] == '5')
            candidate = "90" + digits[1..];
        else if (digits.Length == 10 && digits[0] == '5')
            candidate = "90" + digits;

        if (candidate is null || !Valid12.IsMatch(candidate))
            return false;

        normalized12 = candidate;
        return true;
    }

    private static string ExtractDigits(string input)
    {
        Span<char> buffer = stackalloc char[input.Length];
        var n = 0;
        foreach (var ch in input.Trim())
        {
            if (char.IsDigit(ch))
                buffer[n++] = ch;
        }

        return n == 0 ? string.Empty : new string(buffer[..n]);
    }
}
