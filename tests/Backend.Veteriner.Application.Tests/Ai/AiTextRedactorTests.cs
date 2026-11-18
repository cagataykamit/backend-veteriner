using AwesomeAssertions;
using Backend.Veteriner.Application.Ai;

namespace Backend.Veteriner.Application.Tests.Ai;

public sealed class AiTextRedactorTests
{
    private static readonly IReadOnlyList<string> NoNames = [];

    [Theory]
    [InlineData("Sahibi 0532 123 45 67 numarasından aradı.", "Sahibi [numara] numarasından aradı.")]
    [InlineData("Tel: +90 (532) 123-45-67", "Tel: [numara]")]
    [InlineData("TCKN 12345678901 ile kayıtlı.", "TCKN [numara] ile kayıtlı.")]
    [InlineData("Çip 900123456789012 okundu.", "Çip [numara] okundu.")]
    [InlineData("Yazı: ayse.yilmaz@example.com adresine gitti.", "Yazı: [e-posta] adresine gitti.")]
    public void Masks_Identifiers(string input, string expected)
        => AiTextRedactor.Redact(input, NoNames).Should().Be(expected);

    [Theory]
    [InlineData("Kontrol 12.03.2026 14:30'da yapıldı.")]
    [InlineData("Kilo 4.25 kg, ateş 38.6, WBC 12500.")]
    [InlineData("Aşı 01/02/2026 tarihinde.")]
    [InlineData("Kontrol 2026-03-12 10:00 ve 2026-04-01.")]
    public void Keeps_Dates_And_Clinical_Numbers(string input)
        => AiTextRedactor.Redact(input, NoNames).Should().Be(input);

    [Fact]
    public void Masks_Date_Followed_By_Phone_Number()
        => AiTextRedactor.Redact("12.03.2026 0532 123 45 67 aradı", NoNames).Should().Be("[numara] aradı");

    [Fact]
    public void Masks_Owner_Name_Terms_As_Whole_Words_Case_Insensitive()
    {
        var names = AiTextRedactor.NameTerms("Ayşe Nur Yılmaz");

        var result = AiTextRedactor.Redact("AYŞE hanım aradı; Yılmaz ailesi. Nurlu mama verildi.", names);

        names.Should().BeEquivalentTo(["Ayşe", "Nur", "Yılmaz"]);
        result.Should().Be("[kişi] hanım aradı; [kişi] ailesi. Nurlu mama verildi.");
    }

    [Fact]
    public void Name_Terms_Skip_Short_Parts()
        => AiTextRedactor.NameTerms("Ali Ak").Should().Equal("Ali");

    [Fact]
    public void Null_And_Blank_Pass_Through()
    {
        AiTextRedactor.Redact(null, NoNames).Should().BeNull();
        AiTextRedactor.Redact("  ", NoNames).Should().Be("  ");
    }
}
