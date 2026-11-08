using Backend.Veteriner.Domain.ClinicSites;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.ClinicSites;

/// <summary>Randevu sayfasının markası (APPT-022): yayındaki yapılandırmadan yalnız logo başvurusu ve ana renk okunur.</summary>
public sealed class ClinicSiteBrandTests
{
    private static string Config(string logo, string primary)
        => "{\"schemaVersion\":1,\"content\":{\"clinic\":{\"logo\":" + logo + "}},\"design\":{\"colors\":{\"primary\":" + primary + ",\"accent\":\"#ffffff\"}}}";

    [Fact]
    public void Read_Should_Return_Site_Image_Logo_And_Lowercase_Hex_Color()
    {
        var id = Guid.NewGuid();

        var brand = ClinicSiteBrand.Read(Config($"\"site-image:{id}\"", "\" #0A7D6F \""));

        brand.LogoImageId.Should().Be(id);
        brand.AccentColor.Should().Be("#0a7d6f");
    }

    [Theory]
    [InlineData("\"stock:logo/ornek.png\"", "\"#12345\"")]
    [InlineData("null", "\"red\"")]
    [InlineData("\"site-image:kimlik-degil\"", "\"rgb(0,0,0)\"")]
    [InlineData("12", "null")]
    [InlineData("\"https://ornek.example.com/logo.png\"", "\"#0a7d6f80\"")]
    public void Read_Should_Ignore_Values_Outside_Expected_Shape(string logo, string primary)
    {
        var brand = ClinicSiteBrand.Read(Config(logo, primary));

        brand.LogoImageId.Should().BeNull();
        brand.AccentColor.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"schemaVersion":1}""")]
    [InlineData("""{"schemaVersion":1,"content":[],"design":"x"}""")]
    [InlineData("bozuk")]
    public void Read_Should_Return_None_When_Fields_Missing_Or_Json_Broken(string json)
        => ClinicSiteBrand.Read(json).Should().Be(ClinicSiteBrand.None);
}
