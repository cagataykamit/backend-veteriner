using System.Text;
using Backend.Veteriner.Application.Common;

namespace Backend.Veteriner.Application.Imports.Mapping;

/// <summary>
/// Başlık, sayfa adı ve değer eşlemede tek karşılaştırma anahtarı (IMPORT_API_CONTRACT.md §5): ı/İ katlaması
/// <see cref="DailySearchTerm.Fold"/> ile ortaktır; ardından küçük harf, Türkçe aksan atma ve harf/rakam dışı
/// karakterlerin (boşluk, tire, alt çizgi, nokta) atılması.
/// </summary>
public static class ImportTextFolding
{
    public static string Fold(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var raw in DailySearchTerm.Fold(value))
        {
            var c = char.ToLowerInvariant(raw);
            c = c switch
            {
                'ç' => 'c',
                'ğ' => 'g',
                'ö' => 'o',
                'ş' => 's',
                'ü' => 'u',
                'â' => 'a',
                'î' => 'i',
                'û' => 'u',
                _ => c,
            };

            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }

        return sb.ToString();
    }
}
