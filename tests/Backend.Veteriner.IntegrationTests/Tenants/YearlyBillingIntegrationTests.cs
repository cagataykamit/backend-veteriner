using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Tenants;

/// <summary>
/// BILL-001 uçtan uca (HTTP + LocalDB, Manual sağlayıcı): aylık ücretli kiracı dönem içinde yıllığa geçer, oturum yıllık
/// açılır ve sonuçlandırılınca abonelik bir yıllık döneme başlar; geçersiz dönem değeri 400 döner.
/// Bkz. docs/SUBSCRIPTION_YEARLY_BILLING_API_CONTRACT.md.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class YearlyBillingIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public YearlyBillingIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Monthly_Paid_Tenant_Should_Switch_To_Yearly_And_Start_One_Year_Period()
    {
        // Entegrasyon yapılandırmasında plan fiyatı yok; oranlama için test fiyatları verilir.
        await using var priced = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:PlanPricesMinor:Basic"] = "99900",
                ["Billing:PlanPricesMinor:Pro"] = "199900",
                ["Billing:PlanPricesMinor:Premium"] = "299900",
            })));
        var (tenantId, owner) = await SignupOwnerAsync(priced);
        await ActivateMonthlyAsync(priced, tenantId, DateTime.UtcNow.AddDays(-10));

        var start = await owner.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/subscription-checkout", new { targetPlanCode = "Basic", billingPeriod = "Yearly" });
        var startBody = await start.Content.ReadAsStringAsync();
        start.StatusCode.Should().Be(HttpStatusCode.OK, startBody);
        var session = JsonDocument.Parse(startBody).RootElement;
        session.GetProperty("billingPeriod").GetString().Should().Be("Yearly");
        session.GetProperty("proratedChargeMinor").GetInt64().Should().BeInRange(999_000 - 99_900, 999_000 - 1,
            "yıllık fiyat (10 ay) kullanılmayan aylık günler düşülerek çekilir");
        var sessionId = session.GetProperty("checkoutSessionId").GetGuid();

        var status = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/subscription-checkout/{sessionId}");
        status.GetProperty("billingPeriod").GetString().Should().Be("Yearly");

        var finalize = await owner.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/subscription-checkout/{sessionId}/finalize", new { externalReference = "yillik-ref" });
        finalize.StatusCode.Should().Be(HttpStatusCode.OK, await finalize.Content.ReadAsStringAsync());

        var summary = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/subscription-summary");
        summary.GetProperty("billingPeriod").GetString().Should().Be("Yearly");
        var periodStart = summary.GetProperty("currentPeriodStartUtc").GetDateTime();
        summary.GetProperty("currentPeriodEndUtc").GetDateTime().Should().Be(periodStart.AddYears(1));
        periodStart.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5), "yıllık dönem ödeme anında başlar");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("Weekly")]
    public async Task Start_Checkout_Should_Reject_Invalid_Billing_Period(string billingPeriod)
    {
        var (tenantId, owner) = await SignupOwnerAsync(_factory);

        var response = await owner.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/subscription-checkout", new { targetPlanCode = "Pro", billingPeriod });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Subscriptions.Checkout.Validation.BillingPeriodInvalid");
    }

    private static async Task ActivateMonthlyAsync(WebApplicationFactory<Program> app, Guid tenantId, DateTime paidAtUtc)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == tenantId);
        sub.ActivatePaidPlan(sub.PlanCode, BillingPeriodKind.Monthly, paidAtUtc);
        await db.SaveChangesAsync();
    }

    private static async Task<(Guid TenantId, HttpClient Owner)> SignupOwnerAsync(WebApplicationFactory<Program> app)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"yillik-{suffix}@example.com";
        var documents = LegalDocumentCatalog.CurrentVersions
            .Select(kv => (object)new { documentType = kv.Key.ToString(), version = kv.Value })
            .ToArray();

        var signup = await app.CreateClient().PostAsJsonAsync("/api/v1/public/owner-signup", new
        {
            planCode = "Basic",
            tenantName = $"Yıllık Kiracı {suffix}",
            clinicName = "Yıllık Klinik",
            clinicCity = "İzmir",
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
