using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stripe;

namespace Backend.IntegrationTests.Tenants;

/// <summary>
/// POST /webhooks/billing/{stripe|iyzico}: üretimde aboneliği etkinleştiren tek yol (BILL-002). Uçtan uca: imza
/// doğrulaması, idempotent receipt, etkinleştirme, çift teslim ve işlenemeyen olayın yeniden denemeye açık kalması.
/// Stripe imzası gerçek algoritmayla (Stripe.net <see cref="EventUtility.ComputeSignature"/>) test sırrıyla üretilir.
/// </summary>
public sealed class BillingWebhookIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string StripeSecret = "whsec_integration_test_secret_0123456789";

    private readonly WebApplicationFactory<global::Program> _factory;
    private readonly HttpClient _client;

    public BillingWebhookIntegrationTests(CustomWebApplicationFactory factory)
    {
        // Test ortamı şablonunda Stripe sırrı yok; yalnız bu sınıfın host'una, fabrikanın kaynaklarından sonra eklenir.
        _factory = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Stripe:WebhookSecret"] = StripeSecret
            })));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Stripe_Should_Reject_When_SignatureHeaderMissing()
    {
        var response = await PostStripeAsync(CompletedEvent("evt_nosig", Guid.NewGuid()), signature: null);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Billing.WebhookSignatureMissing");
        (await ReceiptExistsAsync("evt_nosig")).Should().BeFalse("imzasız istek için idempotency kaydı açılmamalı");
    }

    [Fact]
    public async Task Stripe_Should_Reject_When_SignatureInvalid()
    {
        var payload = CompletedEvent("evt_badsig", Guid.NewGuid());
        var response = await PostStripeAsync(payload, signature: Sign(payload, "whsec_wrong_secret"));

        response.IsSuccessStatusCode.Should().BeFalse();
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Billing.WebhookSignatureInvalid");
        (await ReceiptExistsAsync("evt_badsig")).Should().BeFalse();
    }

    [Fact]
    public async Task Stripe_Should_ActivateSubscription_Once_And_AckDuplicate_On_Redelivery()
    {
        var tenantId = await SignupTenantAsync();
        var sessionId = await SeedOpenCheckoutSessionAsync(tenantId, BillingProvider.Stripe);
        var eventId = $"evt_ok_{Guid.NewGuid():N}";
        var payload = CompletedEvent(eventId, sessionId);

        var first = await PostStripeAsync(payload, Sign(payload, StripeSecret));

        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var ack = await first.Content.ReadFromJsonAsync<JsonElement>();
        ack.GetProperty("processed").GetBoolean().Should().BeTrue();
        ack.GetProperty("duplicate").GetBoolean().Should().BeFalse();
        ack.GetProperty("providerEventId").GetString().Should().Be(eventId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sub = await db.TenantSubscriptions.AsNoTracking().SingleAsync(s => s.TenantId == tenantId);
            sub.Status.Should().Be(TenantSubscriptionStatus.Active);
            sub.PlanCode.Should().Be(SubscriptionPlanCode.Pro);
            var session = await db.BillingCheckoutSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
            session.Status.Should().Be(BillingCheckoutSessionStatus.Completed);
            var receipt = await db.BillingWebhookReceipts.AsNoTracking().SingleAsync(r => r.ProviderEventId == eventId);
            receipt.ProcessedAtUtc.Should().NotBeNull();
        }

        // Sağlayıcı aynı olayı yeniden teslim eder: 200 + duplicate, ikinci kez etkinleştirme yok.
        var second = await PostStripeAsync(payload, Sign(payload, StripeSecret));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var ack2 = await second.Content.ReadFromJsonAsync<JsonElement>();
        ack2.GetProperty("duplicate").GetBoolean().Should().BeTrue();
        ack2.GetProperty("processed").GetBoolean().Should().BeFalse();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.BillingWebhookReceipts.AsNoTracking().CountAsync(r => r.ProviderEventId == eventId))
                .Should().Be(1, "aynı olay için tek receipt");
        }
    }

    [Fact]
    public async Task Stripe_Should_AckAndMarkProcessed_When_EventTypeIgnored()
    {
        var tenantId = await SignupTenantAsync();
        var eventId = $"evt_ignored_{Guid.NewGuid():N}";
        var payload = StripeEvent(eventId, "invoice.paid", Guid.NewGuid());

        var response = await PostStripeAsync(payload, Sign(payload, StripeSecret));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("processed").GetBoolean().Should().BeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.BillingWebhookReceipts.AsNoTracking().SingleAsync(r => r.ProviderEventId == eventId)).ProcessedAtUtc.Should().NotBeNull();
        (await db.TenantSubscriptions.AsNoTracking().SingleAsync(s => s.TenantId == tenantId)).Status
            .Should().Be(TenantSubscriptionStatus.Trialing, "ilgisiz olay aboneliğe dokunmaz");
    }

    [Fact]
    public async Task Stripe_Should_KeepReceiptUnprocessed_When_CheckoutSessionMetadataMissing()
    {
        var eventId = $"evt_nometa_{Guid.NewGuid():N}";
        var payload = StripeEvent(eventId, "checkout.session.completed", checkoutSessionId: null);

        var response = await PostStripeAsync(payload, Sign(payload, StripeSecret));

        response.IsSuccessStatusCode.Should().BeFalse();
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Billing.WebhookCheckoutSessionMissing");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var receipt = await db.BillingWebhookReceipts.AsNoTracking().SingleOrDefaultAsync(r => r.ProviderEventId == eventId);
        // Receipt alındı ama işlenmedi: sağlayıcının yeniden teslimi tekrar işlenebilir (duplicate sayılmaz).
        (receipt is null || receipt.ProcessedAtUtc is null).Should().BeTrue();
    }

    [Fact]
    public async Task Iyzico_Should_Reject_Unsigned_Webhook_Outside_Development()
    {
        var response = await _client.PostAsync(
            "/api/v1/webhooks/billing/iyzico",
            new StringContent("{\"iyziEventType\":\"CHECKOUT_FORM_AUTH\",\"status\":\"SUCCESS\",\"paymentConversationId\":\"x\",\"iyziReferenceCode\":\"ref-1\"}", Encoding.UTF8, "application/json"));

        response.IsSuccessStatusCode.Should().BeFalse();
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Billing.WebhookSignatureMissing");
    }

    // ---- yardımcılar ----

    private static string CompletedEvent(string eventId, Guid checkoutSessionId)
        => StripeEvent(eventId, "checkout.session.completed", checkoutSessionId);

    private static string StripeEvent(string eventId, string type, Guid? checkoutSessionId)
    {
        var metadata = checkoutSessionId is { } id
            ? $",\"metadata\":{{\"billing_checkout_session_id\":\"{id:D}\"}}"
            : "";
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"{{\"id\":\"{eventId}\",\"object\":\"event\",\"api_version\":\"2024-06-20\",\"created\":{created},\"livemode\":false," +
               $"\"type\":\"{type}\",\"data\":{{\"object\":{{\"id\":\"cs_test_{eventId}\",\"object\":\"checkout.session\"{metadata}}}}}}}";
    }

    private static string Sign(string payload, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = EventUtility.ComputeSignature(secret, timestamp, payload);
        return $"t={timestamp},v1={signature}";
    }

    private async Task<HttpResponseMessage> PostStripeAsync(string payload, string? signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/billing/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (signature is not null)
            request.Headers.TryAddWithoutValidation("Stripe-Signature", signature);
        return await _client.SendAsync(request);
    }

    private async Task<bool> ReceiptExistsAsync(string eventId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.BillingWebhookReceipts.AsNoTracking().AnyAsync(r => r.ProviderEventId == eventId);
    }

    private async Task<Guid> SignupTenantAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var response = await _client.PostAsJsonAsync("/api/v1/public/owner-signup", new
        {
            planCode = "Basic",
            tenantName = $"Webhook Kiracı {suffix}",
            clinicName = "Webhook Klinik",
            clinicCity = "Ankara",
            email = $"webhook-owner-{suffix}@example.com",
            password = "Abc12345!",
            acceptedLegalDocuments = LegalDocumentCatalog.CurrentVersions
                .Select(kv => new { documentType = kv.Key.ToString(), version = kv.Value })
                .ToArray()
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
    }

    private async Task<Guid> SeedOpenCheckoutSessionAsync(Guid tenantId, BillingProvider provider)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = BillingCheckoutSession.CreatePending(
            tenantId, SubscriptionPlanCode.Basic, SubscriptionPlanCode.Pro, provider, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30));
        db.BillingCheckoutSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }
}
