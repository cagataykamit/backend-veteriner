using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using FluentValidation;

namespace Backend.Veteriner.Application.Imports.Validation;

public enum ImportDateFailure
{
    None = 0,
    Invalid = 1,
    Ambiguous = 2,
}

public readonly record struct ImportDateResult(DateOnly? Value, ImportDateFailure Failure)
{
    public static ImportDateResult Ok(DateOnly value) => new(value, ImportDateFailure.None);
    public static ImportDateResult Fail(ImportDateFailure failure) => new(null, failure);
}

public enum ImportMicrochipProblem
{
    None = 0,
    Invalid = 1,
    ScientificNotation = 2,
}

public readonly record struct ImportMicrochipResult(string? Value, ImportMicrochipProblem Problem);

/// <summary>
/// Hücre değerlerinin ayrıştırılması ve normalizasyonu (IMPORT_API_CONTRACT.md §4.3-4.4). Saf fonksiyonlardır; satır
/// bağlamı (mükerrer, bağlantı) <see cref="ImportValidationEngine"/>'dadır. Telefon ve e-posta mevcut müşteri
/// kurallarını (<see cref="TurkishMobilePhone"/>, FluentValidation <c>EmailAddress</c>) aynen kullanır.
/// </summary>
public static class ImportValueParsers
{
    public const int MaxEmailLength = 320;
    public const int MaxMicrochipLength = 50;
    public const int StandardMicrochipLength = 15;
    public const decimal MaxWeight = 9999.99m;

    /// <summary>Uygulama tarihi alt sınırı (IMPORT_API_CONTRACT.md §4.3).</summary>
    public static readonly DateOnly MinVaccinationDate = new(1990, 1, 1);

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex IntegerWithZeroFraction = new(@"^\d+[.,]0+$", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex ScientificNumber = new(@"^[+-]?\d+([.,]\d+)?[eE][+-]?\d+$", RegexOptions.Compiled, RegexTimeout);
    private const string TimeSuffix = @"(?:[T ]\d{1,2}:\d{2}(?::\d{2}(?:[.,]\d+)?)?)?";

    private static readonly Regex IsoOrder = new(
        @"^(\d{4})[-./](\d{1,2})[-./](\d{1,2})" + TimeSuffix + "$", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex DayFirst = new(
        @"^(\d{1,2})[-./](\d{1,2})[-./](\d{4})" + TimeSuffix + "$", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex TwoDigitYear = new(
        @"^\d{1,2}[-./]\d{1,2}[-./]\d{2}$", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex MonthYear = new(@"^\d{1,2}[-./]\d{4}$", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex ExcelSerial = new(@"^\d{5,7}([.,]\d+)?$", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex HasLetterAndDigit = new(
        @"^(?=.*\p{L})(?=.*\d).+$", RegexOptions.Compiled, RegexTimeout);

    private static readonly InlineValidator<string> EmailRule = BuildEmailRule();

    private static readonly HashSet<string> MaleValues = ["erkek", "e", "male", "m", "er"];
    private static readonly HashSet<string> FemaleValues = ["disi", "d", "female", "f"];
    private static readonly HashSet<string> TrueValues =
        ["evet", "e", "kisir", "kisirlastirilmis", "kisirlastirildi", "1", "true", "yes", "y", "var"];
    private static readonly HashSet<string> FalseValues = ["hayir", "h", "0", "false", "no", "n", "yok"];

    /// <summary>Kırpılmış, art arda boşluğu teke indirilmiş metin; boşsa null.</summary>
    public static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Whitespace.Replace(value.Trim(), " ");
    }

    /// <summary>
    /// Boş değer: <c>(null, false)</c>. Geçerli cep: <c>("905…", false)</c>. Doluysa ama cep standardına uymuyorsa
    /// <c>(null, true)</c> (K5: telefonsuz aktarılır, uyarı verilir).
    /// </summary>
    public static (string? Normalized, bool Invalid) ParsePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (null, false);

        return TurkishMobilePhone.TryNormalize(StripZeroFraction(value.Trim()), out var normalized) && normalized is not null
            ? (normalized, false)
            : (null, true);
    }

    /// <summary>E-posta mevcut müşteri kuralıyla (FluentValidation <c>EmailAddress</c>, ≤ 320) geçerli mi.</summary>
    public static bool IsValidEmail(string value)
        => value.Length <= MaxEmailLength && EmailRule.Validate(value).IsValid;

    /// <summary>
    /// Yalnız-tarih ayrıştırma (bölüm 4.4): <c>gg.aa.yyyy</c> (ayraç <c>. / -</c>), ISO <c>yyyy-aa-gg</c>, saat eki yok
    /// sayılır, Excel seri sayısı. İki haneli yıl, yalnız ay/yıl ve ay adı belirsizdir.
    /// </summary>
    public static ImportDateResult ParseDate(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            return ImportDateResult.Fail(ImportDateFailure.Invalid);

        if (IsoOrder.Match(text) is { Success: true } iso)
            return Build(iso.Groups[1].Value, iso.Groups[2].Value, iso.Groups[3].Value);

        if (DayFirst.Match(text) is { Success: true } dmy)
            return Build(dmy.Groups[3].Value, dmy.Groups[2].Value, dmy.Groups[1].Value);

        if (TwoDigitYear.IsMatch(text) || MonthYear.IsMatch(text))
            return ImportDateResult.Fail(ImportDateFailure.Ambiguous);

        if (ExcelSerial.IsMatch(text)
            && double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
            && serial is >= 1 and <= 2958465)
        {
            return ImportDateResult.Ok(DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(serial))));
        }

        // Ay adı vb. ("3 Mayıs 2024", "May 3, 2024"): yorumu belirsiz.
        if (HasLetterAndDigit.IsMatch(text))
            return ImportDateResult.Fail(ImportDateFailure.Ambiguous);

        return ImportDateResult.Fail(ImportDateFailure.Invalid);

        static ImportDateResult Build(string year, string month, string day)
        {
            try
            {
                return ImportDateResult.Ok(new DateOnly(int.Parse(year), int.Parse(month), int.Parse(day)));
            }
            catch (ArgumentOutOfRangeException)
            {
                return ImportDateResult.Fail(ImportDateFailure.Invalid);
            }
        }
    }

    /// <summary>
    /// Mikroçip: boşluk, <c>-</c> ve <c>.</c> temizlenir; yalnız rakam kalmalı (≤ 50). Bilimsel gösterim ayrı hata.
    /// Boş değer <c>(null, None)</c>.
    /// </summary>
    public static ImportMicrochipResult ParseMicrochip(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            return new(null, ImportMicrochipProblem.None);

        if (ScientificNumber.IsMatch(text))
            return new(null, ImportMicrochipProblem.ScientificNotation);

        text = StripZeroFraction(text);

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is ' ' or '-' or '.' or ' ')
                continue;
            sb.Append(c);
        }

        var cleaned = sb.ToString();
        if (cleaned.Length == 0)
            return new(null, ImportMicrochipProblem.None);

        if (!cleaned.All(char.IsAsciiDigit) || cleaned.Length > MaxMicrochipLength)
            return new(null, ImportMicrochipProblem.Invalid);

        return new(cleaned, ImportMicrochipProblem.None);
    }

    /// <summary>Boş → <c>(null, true)</c>; tanınmayan → <c>(null, false)</c>.</summary>
    public static (PetGender? Gender, bool Recognized) ParseGender(string? value)
    {
        var key = ImportTextFolding.Fold(value);
        if (key.Length == 0)
            return (null, true);
        if (MaleValues.Contains(key))
            return (PetGender.Male, true);
        if (FemaleValues.Contains(key))
            return (PetGender.Female, true);
        return (null, false);
    }

    /// <summary>Boş → <c>(false, true)</c>; tanınmayan → <c>(false, false)</c>.</summary>
    public static (bool Value, bool Recognized) ParseNeutered(string? value)
    {
        var key = ImportTextFolding.Fold(value);
        if (key.Length == 0 || FalseValues.Contains(key))
            return (false, true);
        if (TrueValues.Contains(key))
            return (true, true);
        return (false, false);
    }

    /// <summary>
    /// Ağırlık (kg): ondalık virgül/nokta, <c>kg</c> eki atılır, 0 ≤ değer ≤ 9999,99, 2 ondalık. Boş → <c>(null, true)</c>;
    /// geçersiz → <c>(null, false)</c>.
    /// </summary>
    public static (decimal? Value, bool Valid) ParseWeight(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            return (null, true);

        text = Regex.Replace(text, @"kg\.?$", string.Empty, RegexOptions.IgnoreCase, RegexTimeout).Trim().Replace(" ", string.Empty);

        // "1.234,5" → binlik nokta; "4,5" / "4.5" → ondalık.
        if (text.Contains(',') && text.Contains('.'))
            text = text.Replace(".", string.Empty);
        text = text.Replace(',', '.');

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var weight)
            || weight < 0)
        {
            return (null, false);
        }

        weight = Math.Round(weight, 2, MidpointRounding.AwayFromZero);
        return weight > MaxWeight ? (null, false) : (weight, true);
    }

    /// <summary>Excel sayı hücresi "5321234567.0" gibi okunmuş olabilir; sıfır ondalık atılır.</summary>
    private static string StripZeroFraction(string text)
        => IntegerWithZeroFraction.IsMatch(text) ? text[..text.IndexOfAny(['.', ','])] : text;

    private static InlineValidator<string> BuildEmailRule()
    {
        var rule = new InlineValidator<string>();
        rule.RuleFor(x => x).EmailAddress();
        return rule;
    }
}
