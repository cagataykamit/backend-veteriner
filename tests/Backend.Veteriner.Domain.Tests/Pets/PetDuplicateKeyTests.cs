using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Pets;

/// <summary>Hayvan adı mükerrer anahtarı müşteri adıyla aynı tek kural: Türkçe duyarlı, aksan duyarsız; yalnız karşılaştırma içindir.</summary>
public sealed class PetDuplicateKeyTests
{
    [Theory]
    [InlineData("KARAMEL", "Karamel")]
    [InlineData("ISPARTA", "Isparta")]
    [InlineData("İSPARTA", "isparta")]
    [InlineData("Işık", "IŞIK")]
    [InlineData("Çomar", "COMAR")]
    [InlineData("  Pamuk   Şeker ", "pamuk seker")]
    public void Same_Name_Different_Spelling_Should_Yield_Same_Key(string a, string b)
        => Pet.NormalizeNameForDuplicateCheck(a).Should().Be(Pet.NormalizeNameForDuplicateCheck(b));

    [Theory]
    [InlineData("Pamuk", "Pamuk2")]
    [InlineData("Boncuk", "Bonjuk")]
    public void Different_Names_Should_Yield_Different_Keys(string a, string b)
        => Pet.NormalizeNameForDuplicateCheck(a).Should().NotBe(Pet.NormalizeNameForDuplicateCheck(b));

    [Fact]
    public void Pet_Key_Is_The_Same_Rule_As_Client_Key()
        => Pet.NormalizeNameForDuplicateCheck("İSMAİL ÇAĞLAR")
            .Should().Be(Client.NormalizeFullNameForDuplicateCheck("İSMAİL ÇAĞLAR"));
}
