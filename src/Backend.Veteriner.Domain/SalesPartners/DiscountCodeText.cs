using System.Text;

namespace Backend.Veteriner.Domain.SalesPartners;

/// <summary>
/// İndirim kodu normalizasyonu (ADR-037): büyük/küçük harf duyarsız, boşluksuz, Türkçe harfler ASCII'ye iner
/// (ı/i/İ → I, ş → S, ğ → G, ü → U, ö → O, ç → C). Geçerli kod <c>A-Z 0-9 -</c>, <see cref="MinLength"/>–<see cref="MaxLength"/>.
/// </summary>
public static class DiscountCodeText
{
    public const int MinLength = 3;
    public const int MaxLength = 32;

    /// <summary>Normalize kod; biçim geçersizse <c>null</c>.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsWhiteSpace(c))
                continue;

            var mapped = c switch
            {
                'ı' or 'i' or 'İ' or 'I' or 'î' or 'Î' => 'I',
                'ş' or 'Ş' => 'S',
                'ğ' or 'Ğ' => 'G',
                'ü' or 'Ü' or 'û' or 'Û' => 'U',
                'ö' or 'Ö' => 'O',
                'ç' or 'Ç' => 'C',
                'â' or 'Â' => 'A',
                _ => char.ToUpperInvariant(c),
            };

            if (mapped is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-'))
                return null;

            sb.Append(mapped);
        }

        return sb.Length is >= MinLength and <= MaxLength ? sb.ToString() : null;
    }
}
