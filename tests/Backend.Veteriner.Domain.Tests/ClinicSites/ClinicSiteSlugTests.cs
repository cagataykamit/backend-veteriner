using Backend.Veteriner.Domain.ClinicSites;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.ClinicSites;

/// <summary>Adres kuralı (ADR-045 K10): Türkçe harf dönüşümü, biçim, ayrılmış sözcük.</summary>
public sealed class ClinicSiteSlugTests
{
    [Theory]
    [InlineData("Çağrı Veteriner Kliniği", "cagri-veteriner-klinigi")]
    [InlineData("ÖZEL ŞİŞLİ IŞIK Vet", "ozel-sisli-isik-vet")]
    [InlineData("  Patiler & Dostlar!  ", "patiler-dostlar")]
    [InlineData("Café Vet", "cafe-vet")]
    public void Suggest_Should_Transliterate_And_Hyphenate(string name, string expected)
        => ClinicSiteSlug.Suggest(name).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData(null)]
    [InlineData("Ab")]
    [InlineData("Vetinity")]
    [InlineData("API")]
    public void Suggest_Should_Always_Return_A_Valid_Slug(string? name)
        => ClinicSiteSlug.Check(ClinicSiteSlug.Suggest(name)).Should().Be(ClinicSiteSlugCheck.Valid);

    [Fact]
    public void Suggest_Should_Stay_Within_MaxLength()
    {
        var slug = ClinicSiteSlug.Suggest(new string('a', 100) + " klinik");

        slug.Length.Should().BeLessThanOrEqualTo(ClinicSiteSlug.MaxLength);
        ClinicSiteSlug.Check(slug).Should().Be(ClinicSiteSlugCheck.Valid);
    }

    [Fact]
    public void WithSuffix_Should_Stay_Within_MaxLength_And_Valid()
    {
        var slug = ClinicSiteSlug.WithSuffix(new string('a', ClinicSiteSlug.MaxLength), "12");

        slug.Should().EndWith("-12");
        slug.Length.Should().Be(ClinicSiteSlug.MaxLength);
        ClinicSiteSlug.Check(slug).Should().Be(ClinicSiteSlugCheck.Valid);
    }

    [Theory]
    [InlineData("gecerli-adres", ClinicSiteSlugCheck.Valid)]
    [InlineData("klinik34", ClinicSiteSlugCheck.Valid)]
    [InlineData("ab", ClinicSiteSlugCheck.Invalid)]
    [InlineData("Buyuk", ClinicSiteSlugCheck.Invalid)]
    [InlineData("çağrı", ClinicSiteSlugCheck.Invalid)]
    [InlineData("-basi", ClinicSiteSlugCheck.Invalid)]
    [InlineData("sonu-", ClinicSiteSlugCheck.Invalid)]
    [InlineData("iki--tire", ClinicSiteSlugCheck.Invalid)]
    [InlineData("alt_cizgi", ClinicSiteSlugCheck.Invalid)]
    [InlineData("nokta.lı", ClinicSiteSlugCheck.Invalid)]
    [InlineData("api", ClinicSiteSlugCheck.Reserved)]
    [InlineData("panel", ClinicSiteSlugCheck.Reserved)]
    [InlineData("vetinity", ClinicSiteSlugCheck.Reserved)]
    public void Check_Should_Classify(string slug, ClinicSiteSlugCheck expected)
        => ClinicSiteSlug.Check(slug).Should().Be(expected);

    [Fact]
    public void Check_Should_Reject_Longer_Than_Max()
        => ClinicSiteSlug.Check(new string('a', ClinicSiteSlug.MaxLength + 1)).Should().Be(ClinicSiteSlugCheck.Invalid);
}
