using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Tenants;

/// <summary>
/// WEB-007: tanıtım sitesi paket kataloğu (anonim) ve fiyat tekliği — sitede gösterilen tutar, panel özeti ve checkout'ta
/// çekilen tutar aynı yapılandırmadan aynı kuralla okunur. Bkz. docs/SUBSCRIPTION_PLAN_CATALOG_API_CONTRACT.md.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class PublicSubscriptionPlansIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PublicSubscriptionPlansIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Catalog_Should_Be_Anonymous_And_Match_Backend_Catalog_And_Configured_Prices()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/public/subscription-plans");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("currency").GetString().Should().Be("TRY");
        json.GetProperty("vatIncluded").GetBoolean().Should().BeFalse();
        json.GetProperty("yearlyBilledMonths").GetInt32().Should().Be(SubscriptionPricing.YearlyBilledMonths);

        var billing = _factory.Services.GetRequiredService<IOptions<BillingOptions>>().Value;
        var plans = json.GetProperty("plans").EnumerateArray().ToList();
        plans.Select(p => (p.GetProperty("planCode").GetString()!, p.GetProperty("maxUsers").GetInt32()))
            .Should().Equal(SubscriptionPlanCatalog.All.Select(p => (SubscriptionPlanCatalog.ToApiCode(p.Code), p.MaxUsers)));

        foreach (var (plan, entry) in plans.Zip(SubscriptionPlanCatalog.All))
        {
            ReadNullableInt64(plan, "monthlyPriceMinor").Should().Be(
                SubscriptionPlanPrices.PeriodPriceMinorOrNull(billing, entry.Code, BillingPeriodKind.Monthly));
            ReadNullableInt64(plan, "yearlyPriceMinor").Should().Be(
                SubscriptionPlanPrices.PeriodPriceMinorOrNull(billing, entry.Code, BillingPeriodKind.Yearly));
        }
    }

    [Theory]
    [InlineData("Monthly", "monthlyPriceMinor")]
    [InlineData("Yearly", "yearlyPriceMinor")]
    public async Task Renewal_Charge_And_Panel_Summary_Should_Equal_Public_Price(string billingPeriod, string priceField)
    {
        await using var priced = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:PlanPricesMinor:Basic"] = "123400",
                ["Billing:PlanPricesMinor:Pro"] = "234500",
                ["Billing:PlanPricesMinor:Premium"] = "345600",
            })));
        var publicPro = (await priced.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/public/subscription-plans"))
            .GetProperty("plans").EnumerateArray().Single(p => p.GetProperty("planCode").GetString() == "Pro");
        var publicPrice = publicPro.GetProperty(priceField).GetInt64();
        publicPrice.Should().Be(billingPeriod == "Monthly" ? 234_500 : 234_500L * SubscriptionPricing.YearlyBilledMonths);

        var (tenantId, owner) = await SignupOwnerAsync(priced);
        await ExpireMonthlyPaidPeriodAsync(priced, tenantId);

        var summary = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/subscription-summary");
        summary.GetProperty("availablePlans").EnumerateArray()
            .Single(p => p.GetProperty("code").GetString() == "Pro")
            .GetProperty(priceField).GetInt64().Should().Be(publicPrice, "panel kartı sitedeki fiyatı gösterir");

        var start = await owner.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/subscription-checkout", new { targetPlanCode = "Pro", billingPeriod });
        var body = await start.Content.ReadAsStringAsync();
        start.StatusCode.Should().Be(HttpStatusCode.OK, body);
        JsonDocument.Parse(body).RootElement.GetProperty("proratedChargeMinor").GetInt64()
            .Should().Be(publicPrice, "süresi dolmuş dönemde yenilemede sitedeki dönem fiyatı tam çekilir");
    }

    private static long? ReadNullableInt64(JsonElement element, string name)
        => element.GetProperty(name).ValueKind == JsonValueKind.Null ? null : element.GetProperty(name).GetInt64();

    /// <summary>Aylık ücretli dönem 40 gün önce başlamış: dönem bitmiş, abonelik salt okunur (yenileme tam fiyat).</summary>
    private static async Task ExpireMonthlyPaidPeriodAsync(WebApplicationFactory<Program> app, Guid tenantId)
    {
        await using var scope = app.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == tenantId);
        sub.ActivatePaidPlan(sub.PlanCode, BillingPeriodKind.Monthly, DateTime.UtcNow.AddDays(-40));
        await db.SaveChangesAsync();
    }

    private static async Task<(Guid TenantId, HttpClient Owner)> SignupOwnerAsync(WebApplicationFactory<Program> app)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"paket-{suffix}@example.com";
        var documents = LegalDocumentCatalog.CurrentVersions
            .Select(kv => (object)new { documentType = kv.Key.ToString(), version = kv.Value })
            .ToArray();

        var signup = await app.CreateClient().PostAsJsonAsync("/api/v1/public/owner-signup", new
        {
            planCode = "Basic",
            tenantName = $"Paket Kiracı {suffix}",
            clinicName = "Paket Klinik",
            clinicCity = "Ankara",
            email,
            password = "Abc12345!",
            acceptedLegalDocuments = documents,
        });
        signup.StatusCode.Should().Be(HttpStatusCode.OK, await signup.Content.ReadAsStringAsync());
        var tenantId = (await signup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();

        var owner = app.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(app.Services, email,
                [PermissionCatalog.Subscriptions.Read, PermissionCatalog.Subscriptions.Manage]));
        return (tenantId, owner);
    }
}
