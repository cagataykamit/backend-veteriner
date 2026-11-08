using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Shared;

/// <summary>İl ve ilçe adres biçimi (TIER-015): Türkçe harfler, büyük I ve İ, boşluk ve noktalama.</summary>
public sealed class TurkishSlugTests
{
    [Theory]
    [InlineData("İstanbul", "istanbul")]
    [InlineData("ISTANBUL", "istanbul")]
    [InlineData("İSTANBUL", "istanbul")]
    [InlineData("istanbul", "istanbul")]
    [InlineData("Kadıköy", "kadikoy")]
    [InlineData("KADIKÖY", "kadikoy")]
    [InlineData("Şişli", "sisli")]
    [InlineData("Üsküdar", "uskudar")]
    [InlineData("Çankaya", "cankaya")]
    [InlineData("Iğdır", "igdir")]
    [InlineData("Ağrı", "agri")]
    [InlineData("Gaziosmanpaşa", "gaziosmanpasa")]
    [InlineData("Kahramanmaraş", "kahramanmaras")]
    [InlineData("  19 Mayıs  ", "19-mayis")]
    [InlineData("Afyon-Karahisar", "afyon-karahisar")]
    [InlineData("Bayraklı / İzmir", "bayrakli-izmir")]
    [InlineData("Âşık Veysel", "asik-veysel")]
    public void From_Should_Transliterate_Turkish_Letters(string value, string expected)
        => TurkishSlug.From(value).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void From_Should_Return_Empty_When_Nothing_Remains(string? value)
        => TurkishSlug.From(value).Should().BeEmpty();

    [Fact]
    public void Spellings_Of_The_Same_Place_Should_Share_A_Slug()
        => new[] { "İstanbul", "istanbul", "ISTANBUL", " İstanbul " }.Select(TurkishSlug.From).Distinct().Should().ContainSingle();
}
