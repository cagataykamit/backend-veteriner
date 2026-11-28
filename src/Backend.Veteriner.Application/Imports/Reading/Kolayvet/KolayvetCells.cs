using System.Globalization;
using System.Text.RegularExpressions;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Reading.EVet;

namespace Backend.Veteriner.Application.Imports.Reading.Kolayvet;

/// <summary>
/// Kolayvet dışa aktarma hücreleri (IMPORT_API_CONTRACT.md §15.2). Biçimler gerçek dosyadan (2026-11-28): tarih <c>28 Kasım 2026</c>
/// ya da <c>2026-11-28</c>; telefon hücresinde numara iki kez bitişik ve düğme metniyle; detaylı hasta raporunda birleşik etiketli metin.
/// </summary>
internal static class KolayvetCells
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex MonthName = new(@"^(\d{1,2})\s+(\p{L}+)\s+(\d{4})$", RegexOptions.Compiled, RegexTimeout);

    /// <summary>Türkiye cep numarası; hücrede ilk geçen alınır (<c>0532 123 45 670532 123 45 67 …</c>).</summary>
    private static readonly Regex MobilePhone = new(@"(?:\+?90\s*)?0?\s*5\d{2}\s*\d{3}\s*\d{2}\s*\d{2}", RegexOptions.Compiled, RegexTimeout);

    private static readonly Dictionary<string, int> Months = new(StringComparer.Ordinal)
    {
        ["ocak"] = 1, ["subat"] = 2, ["mart"] = 3, ["nisan"] = 4, ["mayis"] = 5, ["haziran"] = 6,
        ["temmuz"] = 7, ["agustos"] = 8, ["eylul"] = 9, ["ekim"] = 10, ["kasim"] = 11, ["aralik"] = 12,
    };

    /// <summary>Detaylı hasta raporunun etiketleri; değer bir sonraki etikete kadar sürer (hücrede ayraç yok).</summary>
    private static readonly string[] CustomerLabels = ["Tel:", "T.C.:"];

    private static readonly string[] DetailLabels = ["Yaş:", "Doğum tarihi:", "Mikroçip No:", "Tarbil Durumu:", "Kayıt tarihi:"];

    /// <summary>Tarih: <c>gg Ay yyyy</c> (Türkçe ay adı) ya da sayısal biçimler (<see cref="EVetCells.ParseDateTime"/>); okunamazsa null.</summary>
    public static DateOnly? ParseDate(string? value)
    {
        var text = EVetCells.Clean(value);
        if (text is null)
            return null;

        if (MonthName.Match(text) is { Success: true } named)
        {
            if (!Months.TryGetValue(ImportTextFolding.Fold(named.Groups[2].Value), out var month))
                return null;

            var day = int.Parse(named.Groups[1].Value, CultureInfo.InvariantCulture);
            var year = int.Parse(named.Groups[3].Value, CultureInfo.InvariantCulture);
            return day <= DateTime.DaysInMonth(year, month) ? new DateOnly(year, month, day) : null;
        }

        return EVetCells.ParseDateTime(text) is { } date ? DateOnly.FromDateTime(date) : null;
    }

    public static string? IsoDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string? NoteDate(DateOnly? date) => date?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>Hücrede ilk geçen cep numarası; yoksa hücrenin harfe kadar olan kısmı (doğrulama geçersiz telefonu raporlar).</summary>
    public static string? Phone(string? value)
    {
        var text = EVetCells.Clean(value);
        if (text is null)
            return null;

        if (MobilePhone.Match(text) is { Success: true } match)
            return match.Value.Trim();

        var letter = Array.FindIndex(text.ToCharArray(), char.IsLetter);
        return EVetCells.Clean(letter > 0 ? text[..letter] : text);
    }

    /// <summary>Detaylı rapor "Müşteri Bilgileri": <c>Ad Soyad Tel: 0532 …T.C.: …</c> → ad ve telefon.</summary>
    public static (string? Name, string? Phone) CustomerInfo(string? value)
    {
        var text = EVetCells.Clean(value);
        if (text is null)
            return (null, null);

        var parts = Split(text, CustomerLabels);
        return (EVetCells.Clean(parts[string.Empty]), Phone(parts.GetValueOrDefault("Tel:")));
    }

    /// <summary>
    /// Detaylı rapor "Hasta Bilgileri": <c>AD⏎Tür / Irk  Cinsiyet</c> → ad ve tür. Ad ile tür arası satır sonudur (gerçek dosya); satır
    /// sonu yoksa tür ilk parçanın son kelimesidir.
    /// </summary>
    public static (string? Name, string? Species) PetInfo(string? value)
    {
        var first = EVetCells.Clean(value)?.Split('/')[0].Trim();
        if (string.IsNullOrEmpty(first))
            return (null, null);

        var lineBreak = first.IndexOfAny(['\r', '\n']);
        if (lineBreak > 0)
            return (EVetCells.Clean(first[..lineBreak]), EVetCells.Clean(first[lineBreak..]));

        var space = first.LastIndexOf(' ');
        return space < 0 ? (first, null) : (EVetCells.Clean(first[..space]), first[(space + 1)..]);
    }

    /// <summary>Detaylı rapor "Ek Bilgiler" → doğum tarihi, mikroçip, Tarbil durumu, eski programa kayıt tarihi.</summary>
    public static (DateOnly? BirthDate, string? Chip, string? Tarbil, DateOnly? Registered) PetDetails(string? value)
    {
        var text = EVetCells.Clean(value);
        if (text is null)
            return (null, null, null, null);

        var parts = Split(text, DetailLabels);
        return (
            ParseDate(parts.GetValueOrDefault("Doğum tarihi:")),
            EVetCells.Clean(parts.GetValueOrDefault("Mikroçip No:")),
            EVetCells.Clean(parts.GetValueOrDefault("Tarbil Durumu:")),
            ParseDate(parts.GetValueOrDefault("Kayıt tarihi:")));
    }

    /// <summary>Etiketli metni parçalar: ilk etiketten önceki kısım boş anahtarda; her etiketin değeri bir sonraki etikete kadar.</summary>
    private static Dictionary<string, string> Split(string text, string[] labels)
    {
        var found = labels
            .Select(label => (Label: label, Index: text.IndexOf(label, StringComparison.Ordinal)))
            .Where(x => x.Index >= 0)
            .OrderBy(x => x.Index)
            .ToList();
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [string.Empty] = found.Count == 0 ? text : text[..found[0].Index],
        };
        for (var i = 0; i < found.Count; i++)
        {
            var start = found[i].Index + found[i].Label.Length;
            var end = i + 1 < found.Count ? found[i + 1].Index : text.Length;
            parts[found[i].Label] = text[start..end].Trim();
        }

        return parts;
    }
}
