using Backend.Veteriner.Domain.Clients;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Clients;

/// <summary>ADR-019 K4: müşteri mükerrer ad anahtarı Türkçe duyarlı ve aksan duyarsızdır; yalnız karşılaştırma içindir.</summary>
public sealed class ClientDuplicateKeyTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("AYŞE YILMAZ", "Ayşe Yılmaz")]
    [InlineData("AYSE YILMAZ", "Ayşe Yılmaz")]
    [InlineData("ISPARTA", "Isparta")]
    [InlineData("ISPARTA", "ısparta")]
    [InlineData("İSPARTA", "Isparta")]
    [InlineData("İSPARTA", "isparta")]
    [InlineData("ışık", "IŞIK")]
    [InlineData("İşik", "Işık")]
    [InlineData("ŞĞÜÖÇ", "sguoc")]
    [InlineData("şğüöç", "SGUOC")]
    [InlineData("  Ayşe   Yılmaz  ", "ayşe yılmaz")]
    [InlineData("Ayşe\tYılmaz", "Ayşe Yılmaz")]
    public void Same_Person_Different_Spelling_Should_Yield_Same_Key(string a, string b)
        => Client.NormalizeFullNameForDuplicateCheck(a).Should().Be(Client.NormalizeFullNameForDuplicateCheck(b));

    [Theory]
    [InlineData("Ayşe Yılmaz", "Ayşe Yılmazoğlu")]
    [InlineData("Ali Veli", "AliVeli")]
    [InlineData("Ayşe Kaya", "Ayşe Yılmaz")]
    public void Different_Names_Should_Yield_Different_Keys(string a, string b)
        => Client.NormalizeFullNameForDuplicateCheck(a).Should().NotBe(Client.NormalizeFullNameForDuplicateCheck(b));

    [Fact]
    public void Key_Is_Ascii_Lower_Case_Folded()
        => Client.NormalizeFullNameForDuplicateCheck("  İSMAİL   ÇAĞLAR ÖZÜŞIK ").Should().Be("ismail caglar ozusik");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_Should_Yield_Empty_Key(string? value)
        => Client.NormalizeFullNameForDuplicateCheck(value!).Should().BeEmpty();

    [Fact]
    public void Stored_FullName_Is_Not_Altered_By_The_Key()
    {
        var client = new Client(Guid.NewGuid(), "  AYŞE YILMAZ ", Now);
        client.FullName.Should().Be("AYŞE YILMAZ");
    }
}
