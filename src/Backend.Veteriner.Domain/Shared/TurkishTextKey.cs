namespace Backend.Veteriner.Domain.Shared;

/// <summary>
/// Türkçe metin için tek karşılaştırma anahtarı (mükerrer kontrolü ve arama; saklanan metin değişmez):
/// kırp, art arda boşluğu teke indir, Türkçe duyarlı küçült (I/ı/İ/i hepsi "i") ve ç/ğ/ö/ş/ü ile â/î/û aksanlarını at.
/// </summary>
public static class TurkishTextKey
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new System.Text.StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var raw in value.Trim())
        {
            if (char.IsWhiteSpace(raw))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(raw switch
            {
                'ı' or 'İ' or 'I' or 'î' or 'Î' => 'i',
                'ç' or 'Ç' => 'c',
                'ğ' or 'Ğ' => 'g',
                'ö' or 'Ö' => 'o',
                'ş' or 'Ş' => 's',
                'ü' or 'Ü' or 'û' or 'Û' => 'u',
                'â' or 'Â' => 'a',
                _ => char.ToLowerInvariant(raw),
            });
        }

        return sb.ToString();
    }
}
