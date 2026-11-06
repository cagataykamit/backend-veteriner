using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Common.Billing;

/// <summary>WEB-007: plan liste fiyatının tek okunduğu kural (checkout, panel ve tanıtım sitesi aynı kuraldan).</summary>
public sealed class SubscriptionPlanPricesTests
{
    [Fact]
    public void Should_Prefer_PlanPricesMinor_Over_Iyzico_PlanPricesTry()
    {
        var options = new BillingOptions
        {
            PlanPricesMinor = new Dictionary<string, long> { ["Basic"] = 149_000 },
            Iyzico = new IyzicoBillingOptions { PlanPricesTry = new Dictionary<string, decimal> { ["Basic"] = 999.90m } },
        };

        SubscriptionPlanPrices.TryGetMonthlyPriceMinor(options, SubscriptionPlanCode.Basic, out var minor).Should().BeTrue();
        minor.Should().Be(149_000);
    }

    [Fact]
    public void Should_Fall_Back_To_Iyzico_PlanPricesTry_In_Kurus()
    {
        var options = new BillingOptions
        {
            Iyzico = new IyzicoBillingOptions { PlanPricesTry = new Dictionary<string, decimal> { ["pro"] = 2490.50m } },
        };

        SubscriptionPlanPrices.TryGetMonthlyPriceMinor(options, SubscriptionPlanCode.Pro, out var minor).Should().BeTrue();
        minor.Should().Be(249_050, "anahtar büyük/küçük harf duyarsız, TL kuruşa çevrilir");
    }

    [Fact]
    public void Should_Treat_Zero_Or_Missing_Price_As_Not_Configured()
    {
        var options = new BillingOptions
        {
            PlanPricesMinor = new Dictionary<string, long> { ["Basic"] = 0 },
            Iyzico = new IyzicoBillingOptions { PlanPricesTry = new Dictionary<string, decimal> { ["Basic"] = 0m } },
        };

        SubscriptionPlanPrices.TryGetMonthlyPriceMinor(options, SubscriptionPlanCode.Basic, out _).Should().BeFalse();
        SubscriptionPlanPrices.TryGetMonthlyPriceMinor(options, SubscriptionPlanCode.Premium, out _).Should().BeFalse();
        SubscriptionPlanPrices.PeriodPriceMinorOrNull(options, SubscriptionPlanCode.Basic, BillingPeriodKind.Yearly).Should().BeNull();
    }

    [Fact]
    public void Yearly_Price_Should_Follow_YearlyBilledMonths_Rule()
    {
        var options = new BillingOptions { PlanPricesMinor = new Dictionary<string, long> { ["Premium"] = 399_000 } };

        SubscriptionPlanPrices.PeriodPriceMinorOrNull(options, SubscriptionPlanCode.Premium, BillingPeriodKind.Monthly).Should().Be(399_000);
        SubscriptionPlanPrices.PeriodPriceMinorOrNull(options, SubscriptionPlanCode.Premium, BillingPeriodKind.Yearly)
            .Should().Be(399_000 * SubscriptionPricing.YearlyBilledMonths);
    }

    [Theory]
    [InlineData(null, "TRY")]
    [InlineData("  ", "TRY")]
    [InlineData("try", "TRY")]
    public void Currency_Should_Default_To_Try_And_Uppercase(string? configured, string expected)
        => SubscriptionPlanPrices.Currency(new BillingOptions { PlanPriceCurrency = configured! }).Should().Be(expected);
}
