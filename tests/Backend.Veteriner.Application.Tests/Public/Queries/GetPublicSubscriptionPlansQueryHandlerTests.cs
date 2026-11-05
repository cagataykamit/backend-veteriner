using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Public.Queries.SubscriptionPlans;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.SalesPartners;
using AwesomeAssertions;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Tests.Public.Queries;

/// <summary>WEB-007: herkese açık paket kataloğu; sınır katalogdan, fiyat checkout kuralından.</summary>
public sealed class GetPublicSubscriptionPlansQueryHandlerTests
{
    private static Task<Backend.Veteriner.Application.Public.Contracts.Dtos.PublicSubscriptionPlanCatalogDto> HandleAsync(BillingOptions options)
        => new GetPublicSubscriptionPlansQueryHandler(Options.Create(options))
            .Handle(new GetPublicSubscriptionPlansQuery(), CancellationToken.None).AsTask();

    [Fact]
    public async Task Should_Return_Catalog_Plans_With_MaxUsers_And_Prices()
    {
        var options = new BillingOptions
        {
            PlanPricesMinor = new Dictionary<string, long> { ["Basic"] = 149_000, ["Pro"] = 249_000, ["Premium"] = 399_000 },
        };

        var result = await HandleAsync(options);

        result.Currency.Should().Be("TRY");
        result.VatIncluded.Should().BeFalse("fiyatlar KDV hariç");
        result.YearlyBilledMonths.Should().Be(SubscriptionPricing.YearlyBilledMonths);
        result.Plans.Select(p => (p.PlanCode, p.MaxUsers)).Should().Equal(
            SubscriptionPlanCatalog.All.Select(p => (SubscriptionPlanCatalog.ToApiCode(p.Code), p.MaxUsers)));
        result.Plans.Select(p => p.MonthlyPriceMinor).Should().Equal(149_000L, 249_000L, 399_000L);
        result.Plans.Select(p => p.YearlyPriceMinor).Should().Equal(1_490_000L, 2_490_000L, 3_990_000L);
    }

    [Fact]
    public async Task Should_Return_Null_Prices_When_Not_Configured()
    {
        var options = new BillingOptions
        {
            Iyzico = new IyzicoBillingOptions { PlanPricesTry = new Dictionary<string, decimal> { ["Basic"] = 0m, ["Pro"] = 0m, ["Premium"] = 0m } },
        };

        var result = await HandleAsync(options);

        result.Plans.Should().HaveCount(SubscriptionPlanCatalog.All.Count);
        result.Plans.Should().OnlyContain(p => p.MonthlyPriceMinor == null && p.YearlyPriceMinor == null);
    }
}
