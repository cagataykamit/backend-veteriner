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
/// Ödeme sayfasında vazgeçme (HTTP + LocalDB, Manual sağlayıcı): iptal edilen oturum kapanır ve sonraki "ödeme başlat" aynı
/// (sağlayıcıda geçersizleşmiş) sayfayı değil yeni oturumu döner. Kapalı oturumu yeniden iptal etmek 204'tür.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class SubscriptionCheckoutCancelIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SubscriptionCheckoutCancelIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Cancelled_Session_Should_Close_And_Next_Start_Should_Open_A_New_Session()
    {
        await using var priced = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:PlanPricesMinor:Basic"] = "99900",
                ["Billing:PlanPricesMinor:Pro"] = "199900",
                ["Billing:PlanPricesMinor:Premium"] = "299900",
            })));
        var (tenantId, owner) = await SignupOwnerAsync(priced);
        await ActivateMonthlyAsync(priced, tenantId);

        var first = await StartProCheckoutAsync(owner, tenantId);
        var reused = await StartProCheckoutAsync(owner, tenantId);
        reused.Should().Be(first, "açık oturum varken aynı paket için yeni oturum açılmaz");

        var cancel = await owner.PostAsync($"/api/v1/tenants/{tenantId}/subscription-checkout/{first}/cancel", null);
        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());

        var status = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/subscription-checkout/{first}");
        status.GetProperty("status").GetInt32().Should().Be((int)BillingCheckoutSessionStatus.Cancelled);
        status.GetProperty("canContinue").GetBoolean().Should().BeFalse();

        var second = await StartProCheckoutAsync(owner, tenantId);
        second.Should().NotBe(first);

        var again = await owner.PostAsync($"/api/v1/tenants/{tenantId}/subscription-checkout/{first}/cancel", null);
        again.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Cancel_Should_Return_404_For_Unknown_Session()
    {
        var (tenantId, owner) = await SignupOwnerAsync(_factory);

        var response = await owner.PostAsync($"/api/v1/tenants/{tenantId}/subscription-checkout/{Guid.NewGuid()}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Subscriptions.CheckoutSessionNotFound");
    }

    private static async Task<Guid> StartProCheckoutAsync(HttpClient owner, Guid tenantId)
    {
        var start = await owner.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/subscription-checkout", new { targetPlanCode = "Pro" });
        var body = await start.Content.ReadAsStringAsync();
        start.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("checkoutSessionId").GetGuid();
    }

    private static async Task ActivateMonthlyAsync(WebApplicationFactory<Program> app, Guid tenantId)
    {
        await using var scope = app.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == tenantId);
        sub.ActivatePaidPlan(sub.PlanCode, BillingPeriodKind.Monthly, DateTime.UtcNow.AddDays(-10));
        await db.SaveChangesAsync();
    }

    private static async Task<(Guid TenantId, HttpClient Owner)> SignupOwnerAsync(WebApplicationFactory<Program> app)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"odeme-iptal-{suffix}@example.com";
        var documents = LegalDocumentCatalog.CurrentVersions
            .Select(kv => (object)new { documentType = kv.Key.ToString(), version = kv.Value })
            .ToArray();

        var signup = await app.CreateClient().PostAsJsonAsync("/api/v1/public/owner-signup", new
        {
            planCode = "Basic",
            tenantName = $"Ödeme İptal Kiracı {suffix}",
            clinicName = "Ödeme İptal Klinik",
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
