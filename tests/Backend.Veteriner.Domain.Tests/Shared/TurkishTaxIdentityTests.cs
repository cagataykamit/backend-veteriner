using Backend.Veteriner.Domain.Shared;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Shared;

/// <summary>ADR-036: TCKN/VKN resmî sağlama algoritmaları ve maskeleme (elle hesaplanmış test vektörleri).</summary>
public sealed class TurkishTaxIdentityTests
{
    [Theory]
    [InlineData("10000000146")]
    [InlineData("12345678950")]
    [InlineData("23456789138")]
    [InlineData("19090909018")] // 10. hane hesabında negatif fark (7×1 − 36) mod 10 = 1
    [InlineData(" 10000000146 ")]
    public void NationalId_Should_Accept_Valid_Numbers(string value)
        => TurkishTaxIdentity.IsValidNationalId(value).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345678901")] // 10. ve 11. hane tutmuyor
    [InlineData("12345678940")] // 10. hane yanlış
    [InlineData("12345678951")] // 11. hane yanlış
    [InlineData("02345678950")] // ilk hane 0
    [InlineData("1000000014")] // 10 hane
    [InlineData("100000001460")] // 12 hane
    [InlineData("1000000014a")]
    [InlineData("100 0000146")]
    public void NationalId_Should_Reject_Invalid_Numbers(string? value)
        => TurkishTaxIdentity.IsValidNationalId(value).Should().BeFalse();

    [Theory]
    [InlineData("1234567890")]
    [InlineData("9876543217")]
    [InlineData("0000000001")] // t = 9 dalı (ilk hane 0 → (0 + 9) mod 10 = 9)
    [InlineData(" 1234567890 ")]
    public void TaxNumber_Should_Accept_Valid_Numbers(string value)
        => TurkishTaxIdentity.IsValidTaxNumber(value).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567891")]
    [InlineData("9876543210")]
    [InlineData("0000000000")]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("12345678a0")]
    public void TaxNumber_Should_Reject_Invalid_Numbers(string? value)
        => TurkishTaxIdentity.IsValidTaxNumber(value).Should().BeFalse();

    [Theory]
    [InlineData("1234567890", true)]
    [InlineData("10000000146", true)]
    [InlineData("1234567891", false)]
    [InlineData("10000000147", false)]
    [InlineData("123456789012", false)]
    public void TaxpayerIdentity_Should_Choose_Rule_By_Length(string value, bool expected)
        => TurkishTaxIdentity.IsValidTaxpayerIdentity(value).Should().Be(expected);

    [Theory]
    [InlineData("12345678950", "123******50")]
    [InlineData("1234567890", "123*****90")]
    [InlineData(" 1234567890 ", "123*****90")]
    [InlineData("123456", "123*56")]
    [InlineData("12345", "*****")]
    [InlineData("1", "*")]
    public void Mask_Should_Show_First_Three_And_Last_Two(string value, string expected)
        => TurkishTaxIdentity.Mask(value).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Mask_Should_Return_Null_For_Empty(string? value)
        => TurkishTaxIdentity.Mask(value).Should().BeNull();
}
