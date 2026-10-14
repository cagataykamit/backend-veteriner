using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>Hücre ayrıştırma kuralları (IMPORT_API_CONTRACT.md §4.3-4.4, §6.4); saf fonksiyonlar.</summary>
public sealed class ImportValueParsersTests
{
    // ---------------------------------------------------------------- Telefon (K5)

    [Theory]
    [InlineData("05321234567", "905321234567")]
    [InlineData("5321234567", "905321234567")]
    [InlineData("+90 532 123 45 67", "905321234567")]
    [InlineData("0 (532) 123-45-67", "905321234567")]
    [InlineData("905321234567", "905321234567")]
    [InlineData("5321234567.0", "905321234567")] // Excel sayı hücresi
    public void Phone_Should_Normalize_Mobile_Formats(string input, string expected)
    {
        var (normalized, invalid) = ImportValueParsers.ParsePhone(input);

        normalized.Should().Be(expected);
        invalid.Should().BeFalse();
    }

    [Theory]
    [InlineData("02121234567")] // sabit hat
    [InlineData("0 212 123 45 67")]
    [InlineData("12345")]
    [InlineData("+44 7700 900123")] // yabancı
    [InlineData("abc")]
    [InlineData("0532 123 45")] // eksik hane
    public void Phone_Should_Flag_Landline_And_Invalid_As_Invalid(string input)
    {
        var (normalized, invalid) = ImportValueParsers.ParsePhone(input);

        normalized.Should().BeNull();
        invalid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Phone_Empty_Should_Be_Neither_Value_Nor_Invalid(string? input)
    {
        ImportValueParsers.ParsePhone(input).Should().Be(((string?)null, false));
    }

    // ---------------------------------------------------------------- E-posta

    [Theory]
    [InlineData("ayse@example.com", true)]
    [InlineData("Ayse.Yilmaz+x@Example.CO", true)]
    [InlineData("ayse@", false)]
    [InlineData("@example.com", false)]
    [InlineData("ayse example.com", false)]
    [InlineData("ayse", false)]
    public void Email_Should_Use_Existing_Client_Rule(string input, bool valid)
    {
        ImportValueParsers.IsValidEmail(input).Should().Be(valid);
    }

    [Fact]
    public void Email_Longer_Than_320_Should_Be_Invalid()
    {
        var local = new string('a', 315);

        ImportValueParsers.IsValidEmail($"{local}@x.com").Should().BeFalse();
    }

    // ---------------------------------------------------------------- Tarih

    [Theory]
    [InlineData("10.04.2024", 2024, 4, 10)]
    [InlineData("3.5.2024", 2024, 5, 3)]
    [InlineData("03/05/2024", 2024, 5, 3)]
    [InlineData("03-05-2024", 2024, 5, 3)]
    [InlineData("2024-05-03", 2024, 5, 3)]
    [InlineData("2024-05-03T14:30:00", 2024, 5, 3)]
    [InlineData("2024.5.3", 2024, 5, 3)]
    [InlineData("03.05.2024 10:15", 2024, 5, 3)]
    [InlineData("45292", 2024, 1, 1)] // Excel seri sayısı
    [InlineData("45292.75", 2024, 1, 1)]
    public void Date_Should_Parse_Day_First_Iso_And_Excel_Serial(string input, int y, int m, int d)
    {
        var result = ImportValueParsers.ParseDate(input);

        result.Failure.Should().Be(ImportDateFailure.None);
        result.Value.Should().Be(new DateOnly(y, m, d));
    }

    [Fact]
    public void Date_Day_First_Should_Not_Be_Read_As_Month_First()
    {
        // 03.05.2024 = 3 Mayıs (5 Mart değil): geri dönüşsüz klinik veri hatasını önleyen kural.
        ImportValueParsers.ParseDate("03.05.2024").Value.Should().Be(new DateOnly(2024, 5, 3));
    }

    [Theory]
    [InlineData("3.5.24")] // iki haneli yıl
    [InlineData("05.2024")] // yalnız ay/yıl
    [InlineData("3 Mayıs 2024")] // ay adı
    [InlineData("May 3 2024")]
    public void Date_Ambiguous_Formats_Should_Be_Rejected_As_Ambiguous(string input)
    {
        var result = ImportValueParsers.ParseDate(input);

        result.Value.Should().BeNull();
        result.Failure.Should().Be(ImportDateFailure.Ambiguous);
    }

    [Theory]
    [InlineData("31.02.2024")]
    [InlineData("32.01.2024")]
    [InlineData("13/25/2024")]
    [InlineData("abc")]
    [InlineData("2024-13-01")]
    [InlineData("20240503")] // yyyyMMdd desteklenmez
    [InlineData("")]
    public void Date_Impossible_Or_Unparseable_Should_Be_Invalid(string input)
    {
        var result = ImportValueParsers.ParseDate(input);

        result.Value.Should().BeNull();
        result.Failure.Should().Be(ImportDateFailure.Invalid);
    }

    // ---------------------------------------------------------------- Mikroçip (K10 biçimi)

    [Theory]
    [InlineData("985112345678901", "985112345678901")]
    [InlineData("985 112 345 678 901", "985112345678901")]
    [InlineData("985-112-345-678-901", "985112345678901")]
    [InlineData("985112345678901.0", "985112345678901")]
    [InlineData("  985112345678901 ", "985112345678901")]
    public void Microchip_Should_Strip_Separators(string input, string expected)
    {
        var result = ImportValueParsers.ParseMicrochip(input);

        result.Problem.Should().Be(ImportMicrochipProblem.None);
        result.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("9,85121E+14")]
    [InlineData("9.85121E+14")]
    [InlineData("9.85121e14")]
    public void Microchip_Scientific_Notation_Should_Be_Its_Own_Problem(string input)
    {
        ImportValueParsers.ParseMicrochip(input).Problem.Should().Be(ImportMicrochipProblem.ScientificNotation);
    }

    [Theory]
    [InlineData("98511234567890A")]
    [InlineData("ABC")]
    public void Microchip_Non_Digits_Should_Be_Invalid(string input)
    {
        ImportValueParsers.ParseMicrochip(input).Problem.Should().Be(ImportMicrochipProblem.Invalid);
    }

    [Fact]
    public void Microchip_Over_50_Digits_Should_Be_Invalid()
    {
        ImportValueParsers.ParseMicrochip(new string('1', 51)).Problem.Should().Be(ImportMicrochipProblem.Invalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" - ")]
    public void Microchip_Empty_Should_Be_Null_Without_Problem(string? input)
    {
        var result = ImportValueParsers.ParseMicrochip(input);

        result.Value.Should().BeNull();
        result.Problem.Should().Be(ImportMicrochipProblem.None);
    }

    // ---------------------------------------------------------------- Cinsiyet / kısır / ağırlık

    [Theory]
    [InlineData("Erkek", PetGender.Male)]
    [InlineData("E", PetGender.Male)]
    [InlineData("male", PetGender.Male)]
    [InlineData("Dişi", PetGender.Female)]
    [InlineData("DİŞİ", PetGender.Female)]
    [InlineData("D", PetGender.Female)]
    [InlineData("F", PetGender.Female)]
    public void Gender_Should_Map_Known_Values(string input, PetGender expected)
    {
        ImportValueParsers.ParseGender(input).Should().Be(((PetGender?)expected, true));
    }

    [Fact]
    public void Gender_Empty_Is_Recognized_Null_And_Unknown_Is_Unrecognized()
    {
        ImportValueParsers.ParseGender(null).Should().Be(((PetGender?)null, true));
        ImportValueParsers.ParseGender("belirsiz").Should().Be(((PetGender?)null, false));
    }

    [Theory]
    [InlineData("Evet", true, true)]
    [InlineData("E", true, true)]
    [InlineData("Kısır", true, true)]
    [InlineData("Kısırlaştırılmış", true, true)]
    [InlineData("1", true, true)]
    [InlineData("True", true, true)]
    [InlineData("Hayır", false, true)]
    [InlineData("H", false, true)]
    [InlineData("0", false, true)]
    [InlineData("", false, true)]
    [InlineData("belki", false, false)]
    public void Neutered_Should_Parse_Common_Values(string input, bool value, bool recognized)
    {
        ImportValueParsers.ParseNeutered(input).Should().Be((value, recognized));
    }

    [Theory]
    [InlineData("4,5", 4.5)]
    [InlineData("4.5", 4.5)]
    [InlineData("12 kg", 12)]
    [InlineData("12,345kg", 12.35)]
    [InlineData("1.234,5", 1234.5)]
    [InlineData("0", 0)]
    public void Weight_Should_Parse_Decimal_Comma_And_Unit(string input, double expected)
    {
        var (value, valid) = ImportValueParsers.ParseWeight(input);

        valid.Should().BeTrue();
        value.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-3")]
    [InlineData("10000")]
    public void Weight_Invalid_Should_Be_Flagged(string input)
    {
        ImportValueParsers.ParseWeight(input).Should().Be(((decimal?)null, false));
    }

    [Fact]
    public void Weight_Empty_Is_Valid_Null()
    {
        ImportValueParsers.ParseWeight(null).Should().Be(((decimal?)null, true));
    }

    // ---------------------------------------------------------------- Metin

    [Fact]
    public void NormalizeText_Should_Trim_And_Collapse_Whitespace()
    {
        ImportValueParsers.NormalizeText("  Ayşe   Yılmaz \t").Should().Be("Ayşe Yılmaz");
        ImportValueParsers.NormalizeText("   ").Should().BeNull();
        ImportValueParsers.NormalizeText(null).Should().BeNull();
    }
}
