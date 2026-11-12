using System.Globalization;
using System.Text.RegularExpressions;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Imports.Reading.EVet;

/// <summary>
/// E-vet hücre biçimleri: yedekte tarih hücresi (okuyucu <c>yyyy-MM-dd[THH:mm:ss]</c> verir) veya Excel seri sayısı, raporda
/// <c>gg.aa.yyyy[ SS:dd]</c>; tutar ve ağırlık kayan noktalı sayı (<c>1250.0000000001</c>). Değerler kanonik tablonun
/// beklediği biçime çevrilir; kural denetimi doğrulama motorundadır.
/// </summary>
internal static class EVetCells
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex Iso = new(@"^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2})(?::(\d{2}))?)?$", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex DayFirst = new(@"^(\d{1,2})\.(\d{1,2})\.(\d{4})(?:\s+(\d{1,2}):(\d{2})(?::(\d{2}))?)?$", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex Serial = new(@"^\d{5}(?:\.\d+)?$", RegexOptions.Compiled, RegexTimeout);

    /// <summary>Sahip + hasta adı anahtarı; doğrulama motorunun ad kuralıyla aynı (Türkçe duyarlı, boşluk ve büyük harf farksız).</summary>
    public static string PairKey(string? ownerName, string? petName)
        => $"{Client.NormalizeFullNameForDuplicateCheck(ImportValueParsers.NormalizeText(ownerName) ?? string.Empty)}\u001f"
           + Pet.NormalizeNameForDuplicateCheck(ImportValueParsers.NormalizeText(petName) ?? string.Empty);

    public static string OwnerKey(string? ownerName)
        => Client.NormalizeFullNameForDuplicateCheck(ImportValueParsers.NormalizeText(ownerName) ?? string.Empty);

    /// <summary>Yerel (İstanbul) tarih-saat; okunamazsa null.</summary>
    public static DateTime? ParseDateTime(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        if (Iso.Match(text) is { Success: true } iso)
            return Build(iso.Groups[1].Value, iso.Groups[2].Value, iso.Groups[3].Value, iso.Groups[4].Value, iso.Groups[5].Value, iso.Groups[6].Value);

        if (DayFirst.Match(text) is { Success: true } dmy)
            return Build(dmy.Groups[3].Value, dmy.Groups[2].Value, dmy.Groups[1].Value, dmy.Groups[4].Value, dmy.Groups[5].Value, dmy.Groups[6].Value);

        if (Serial.IsMatch(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
        {
            var date = DateTime.FromOADate(serial);
            return new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, 0, DateTimeKind.Unspecified);
        }

        return null;

        static DateTime? Build(string year, string month, string day, string hour, string minute, string second)
        {
            try
            {
                return new DateTime(
                    int.Parse(year, CultureInfo.InvariantCulture),
                    int.Parse(month, CultureInfo.InvariantCulture),
                    int.Parse(day, CultureInfo.InvariantCulture),
                    hour.Length == 0 ? 0 : int.Parse(hour, CultureInfo.InvariantCulture),
                    minute.Length == 0 ? 0 : int.Parse(minute, CultureInfo.InvariantCulture),
                    second.Length == 0 ? 0 : int.Parse(second, CultureInfo.InvariantCulture),
                    DateTimeKind.Unspecified);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
    }

    /// <summary>Tarih hücresini <c>yyyy-MM-dd</c> yapar; okunamazsa özgün değer kalır (motor hatayı raporlar).</summary>
    public static string? Date(string? value)
        => ParseDateTime(value) is { } date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Clean(value);

    /// <summary>Not metnine yazılan tarih (<c>gg.aa.yyyy</c>); okunamazsa null (yazılmaz).</summary>
    public static string? NoteDate(string? value)
        => ParseDateTime(value) is { } date ? date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : null;

    /// <summary>Onay kutusu hücresi: yedekte <c>TRUE</c>/<c>FALSE</c> (okuyucu <c>true</c>/<c>false</c> da verir).</summary>
    public static bool IsChecked(string? value) => ImportTextFolding.Fold(value) is "true" or "1" or "evet";

    /// <summary>Tutar: 2 ondalığa yuvarlanır, kuruş altı artık (<c>0,0000001</c>) sıfır sayılır ve boş döner.</summary>
    public static string? Amount(string? value)
    {
        if (!TryNumber(value, out var number))
            return Clean(value);

        var rounded = Math.Round(number, 2, MidpointRounding.AwayFromZero);
        return rounded == 0 ? null : rounded.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>Ağırlık: 2 ondalık; 0 E-vet'te "ölçülmedi" demektir ve boş döner.</summary>
    public static string? Weight(string? value)
    {
        if (!TryNumber(value, out var number))
            return Clean(value);

        var rounded = Math.Round(number, 2, MidpointRounding.AwayFromZero);
        return rounded <= 0 ? null : rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>Vücut sıcaklığı (<c>38.50000000</c>): 1 ondalık; 0 boş.</summary>
    public static string? Temperature(string? value)
    {
        if (!TryNumber(value, out var number))
            return Clean(value);

        var rounded = Math.Round(number, 1, MidpointRounding.AwayFromZero);
        return rounded <= 0 ? null : rounded.ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>Yalnız rakamlar (kimlik, vergi no); rakam yoksa özgün değer.</summary>
    public static string? Digits(string? value)
    {
        var text = Clean(value);
        if (text is null)
            return null;

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? text : digits;
    }

    public static bool IsInteger(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Trim().All(char.IsAsciiDigit);

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Dolu parçaları <paramref name="separator"/> ile birleştirir; hepsi boşsa null.</summary>
    public static string? Join(string separator, params string?[] parts)
    {
        var filled = parts.Select(Clean).Where(p => p is not null).ToList();
        return filled.Count == 0 ? null : string.Join(separator, filled);
    }

    private static bool TryNumber(string? value, out decimal number)
    {
        number = 0;
        var text = value?.Trim();
        return !string.IsNullOrEmpty(text)
               && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }
}
