using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Shared;

public sealed class TaxRatePercentTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(17.5)]
    [InlineData(100)]
    public void IsValid_Should_Accept_Percent_With_Two_Decimals(double rate)
        => TaxRatePercent.IsValid((decimal)rate).Should().BeTrue();

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(20.555)]
    public void IsValid_Should_Reject_Out_Of_Range_Or_Precision(double rate)
        => TaxRatePercent.IsValid((decimal)rate).Should().BeFalse();

    [Fact]
    public void Default_Vat_Rate_Should_Be_Twenty_And_Optional_Null_Valid()
    {
        TaxRatePercent.DefaultVatRate.Should().Be(20m);
        TaxRatePercent.IsValidOptional(null).Should().BeTrue();
    }
}
