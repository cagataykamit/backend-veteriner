using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.SalesPartners;

/// <summary>
/// ADR-037 uçtan uca (HTTP + LocalDB): platform yöneticisi yetkisi (klinik rolleri 403), ortak/kod yönetimi, anonim doğrulamanın
/// tek tip yanıtı ve hız sınırı, kayıtta kod kullanımı, tahsilat olayı simülasyonu (webhook kaynağı) ile komisyon, rapor,
/// ödendi/iptal, CSV ve denetim kaydı. Bkz. docs/SALES_PARTNER_DISCOUNT_API_CONTRACT.md.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class SalesPartnerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminEmail = "admin@example.com";
    private const string Base = "/api/v1/admin/sales-partners";

    private readonly CustomWebApplicationFactory _factory;

    public SalesPartnerIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Admin_Endpoints_Should_Require_Platform_Admin_Claim()
    {
        var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Klinik yöneticisi: tüm izinler olsa da platform_admin claim'i yoksa 403.
        var clinicAdmin = await ClientAsync(platformAdmin: false);
        (await clinicAdmin.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await clinicAdmin.PostAsJsonAsync(Base, new { name = "Yetkisiz" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await clinicAdmin.GetAsync($"{Base}/export.csv")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var platform = await ClientAsync(platformAdmin: true);
        (await platform.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Validate_Should_Return_Uniform_Invalid_And_Never_Expose_Partner()
    {
        var platform = await ClientAsync(platformAdmin: true);
        var partnerId = await CreatePartnerAsync(platform, "Gizli Ortak Adı");
        var code = UniqueCode();
        var codeId = await CreateCodeAsync(platform, partnerId, code.ToLowerInvariant(), 15m, 25m);

        var anonymous = _factory.CreateClient();
        var valid = await ValidateAsync(anonymous, $"  {code.ToLowerInvariant()} ");
        valid.GetProperty("valid").GetBoolean().Should().BeTrue();
        valid.GetProperty("code").GetString().Should().Be(code);
        valid.GetProperty("discountPercent").GetDecimal().Should().Be(15m);
        valid.GetProperty("discountMonths").GetInt32().Should().Be(12);
        valid.GetRawText().Should().NotContain("Gizli").And.NotContain(partnerId.ToString()).And.NotContain("commission");

        var unknown = (await ValidateAsync(anonymous, "YOK-BOYLE-KOD")).GetRawText();
        (await platform.PostAsync($"{Base}/{partnerId}/codes/{codeId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var deactivated = (await ValidateAsync(anonymous, code)).GetRawText();
        var malformed = (await ValidateAsync(anonymous, "!!")).GetRawText();
        var empty = await anonymous.PostAsJsonAsync("/api/v1/public/discount-codes/validate", new { });

        deactivated.Should().Be(unknown);
        malformed.Should().Be(unknown);
        (await empty.Content.ReadAsStringAsync()).Should().Be(unknown);
        JsonDocument.Parse(unknown).RootElement.GetProperty("valid").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Validate_Should_Be_Rate_Limited_Per_Ip()
    {
        using var limited = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Enabled"] = "true" })));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/v1/public/discount-codes/validate", new { code = "DENEME" })).StatusCode);

        statuses.Take(10).Should().OnlyContain(s => s == HttpStatusCode.OK);
        statuses.Skip(10).Should().OnlyContain(s => s == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Code_Admin_Should_Normalize_Reject_Duplicates_And_Write_Audit()
    {
        var platform = await ClientAsync(platformAdmin: true);
        var partnerId = await CreatePartnerAsync(platform, "Denetim Ortak");
        var code = UniqueCode();
        var codeId = await CreateCodeAsync(platform, partnerId, code, 10m, 20m);

        var duplicate = await platform.PostAsJsonAsync($"{Base}/{partnerId}/codes",
            new { code = $" {code.ToLowerInvariant()} ", discountPercent = 5, commissionPercent = 5 });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(duplicate)).Should().Be("DiscountCodes.DuplicateCode");

        var invalid = await platform.PostAsJsonAsync($"{Base}/{partnerId}/codes", new { code = "X", discountPercent = 100, commissionPercent = 5 });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var updated = await platform.PutAsJsonAsync($"{Base}/{partnerId}/codes/{codeId}",
            new { discountPercent = 12.5m, commissionPercent = 30, maxRedemptions = 3 });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedBody = await updated.Content.ReadFromJsonAsync<JsonElement>();
        updatedBody.GetProperty("discountPercent").GetDecimal().Should().Be(12.5m);
        updatedBody.GetProperty("code").GetString().Should().Be(code);

        (await platform.PostAsync($"{Base}/{partnerId}/codes/{codeId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await platform.GetFromJsonAsync<JsonElement>($"{Base}/{partnerId}");
        var codeRow = detail.GetProperty("codes").EnumerateArray().Single();
        codeRow.GetProperty("isActive").GetBoolean().Should().BeFalse();
        detail.GetProperty("partner").GetProperty("codeCount").GetInt32().Should().Be(1);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action.StartsWith("DiscountCode.") && a.Success && a.TargetId != null
                        && (a.TargetId == $"SalesPartnerId={partnerId}" || a.TargetId == $"DiscountCodeId={codeId}"))
            .Select(a => a.Action)
            .ToListAsync();
        actions.Should().Contain(["DiscountCode.Create", "DiscountCode.Update", "DiscountCode.Deactivate"]);
    }

    [Fact]
    public async Task Signup_With_Code_Then_Collection_Event_Should_Produce_Commission_Report_And_Csv()
    {
        var platform = await ClientAsync(platformAdmin: true);
        var partnerId = await CreatePartnerAsync(platform, "Rapor Ortak", email: "rapor-ortak@example.com");
        var code = UniqueCode();
        await CreateCodeAsync(platform, partnerId, code, 10m, 20m);

        // Geçersiz kod ve ortağın kendi e-postası: tek tip 400, kayıt yok.
        var invalidSignup = await SignupAsync("YOK-KOD-XYZ");
        invalidSignup.Response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(invalidSignup.Response)).Should().Be("DiscountCodes.Invalid");
        var selfSignup = await SignupAsync(code, email: "RAPOR-ORTAK@example.com");
        selfSignup.Response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(selfSignup.Response)).Should().Be("DiscountCodes.Invalid");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.AnyAsync(u => u.Email == invalidSignup.Email || u.Email == selfSignup.Email)).Should().BeFalse();
        }

        var signup = await SignupAsync(code.ToLowerInvariant());
        var signupBody = await signup.Response.Content.ReadAsStringAsync();
        signup.Response.StatusCode.Should().Be(HttpStatusCode.OK, signupBody);
        var signupJson = JsonDocument.Parse(signupBody).RootElement;
        signupJson.GetProperty("discount").GetProperty("discountPercent").GetDecimal().Should().Be(10m);
        var tenantId = signupJson.GetProperty("tenantId").GetGuid();

        // Kiracı görünümü: kod uygulanmış, ortak bilgisi yok; ikinci kod reddedilir.
        var owner = _factory.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, signup.Email,
                [PermissionCatalog.Subscriptions.Read, PermissionCatalog.Subscriptions.Manage]));
        var discount = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/subscription-discount");
        discount.GetProperty("applied").GetBoolean().Should().BeTrue();
        discount.GetProperty("code").GetString().Should().Be(code);
        discount.GetRawText().Should().NotContain("Rapor Ortak").And.NotContain("commission");
        var second = await owner.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/subscription-discount", new { code });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(second)).Should().Be("DiscountCodes.DuplicateRedemption");

        // Tahsilat olayı simülasyonu: fiyatlanmış checkout oturumu, imzası doğrulanmış webhook kaynağıyla etkinleştirilir.
        var sessionId = await SimulateCollectionAsync(tenantId, grossMinor: 99_900, discountMinor: 9_990, BillingActivationSource.Webhook);
        // Doğrulanmamış panel finalize'ı komisyon üretmez.
        await SimulateCollectionAsync(tenantId, grossMinor: 99_900, discountMinor: 9_990, BillingActivationSource.Manual);

        var rows = await platform.GetFromJsonAsync<JsonElement>($"{Base}/{partnerId}/redemptions");
        var row = rows.EnumerateArray().Single();
        row.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
        row.GetProperty("customerStatus").GetString().Should().Be("Paid");
        var collection = row.GetProperty("collections").EnumerateArray().Single();
        collection.GetProperty("checkoutSessionId").GetGuid().Should().Be(sessionId);
        collection.GetProperty("netAmountMinor").GetInt64().Should().Be(89_910);
        collection.GetProperty("commissionAmountMinor").GetInt64().Should().Be(17_982);
        collection.GetProperty("status").GetString().Should().Be("Accrued");
        var collectionId = collection.GetProperty("id").GetGuid();

        var summary = (await platform.GetFromJsonAsync<JsonElement>($"{Base}/{partnerId}")).GetProperty("partner").GetProperty("summary");
        summary.GetProperty("redemptionCount").GetInt32().Should().Be(1);
        summary.GetProperty("paidCustomerCount").GetInt32().Should().Be(1);
        summary.GetProperty("revenueNetMinor").GetInt64().Should().Be(89_910);
        summary.GetProperty("commissionPendingMinor").GetInt64().Should().Be(17_982);

        var paid = await platform.PostAsJsonAsync($"{Base}/{partnerId}/commissions/mark-paid",
            new { collectionIds = new[] { collectionId }, paidAtUtc = "2026-11-30T00:00:00Z", note = "Havale" });
        paid.StatusCode.Should().Be(HttpStatusCode.OK);
        (await paid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("updatedCount").GetInt32().Should().Be(1);
        var paidAgain = await platform.PostAsJsonAsync($"{Base}/{partnerId}/commissions/mark-paid",
            new { collectionIds = new[] { collectionId }, paidAtUtc = "2026-11-30T00:00:00Z" });
        paidAgain.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var cancelled = await platform.PostAsJsonAsync($"{Base}/{partnerId}/collections/{collectionId}/cancel", new { reason = "iyzico iadesi" });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = (await platform.GetFromJsonAsync<JsonElement>($"{Base}/{partnerId}")).GetProperty("partner").GetProperty("summary");
        after.GetProperty("commissionEarnedMinor").GetInt64().Should().Be(0);
        after.GetProperty("commissionClawbackMinor").GetInt64().Should().Be(17_982);
        after.GetProperty("paidCustomerCount").GetInt32().Should().Be(0);

        var csv = await platform.GetAsync($"{Base}/export.csv?partnerId={partnerId}");
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var csvText = await csv.Content.ReadAsStringAsync();
        csvText.Should().Contain("Rapor Ortak").And.Contain(code).And.Contain("899,10").And.Contain("179,82").And.Contain("geri alınacak");

        await using var auditScope = _factory.Services.CreateAsyncScope();
        var auditDb = auditScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await auditDb.AuditLogs.AsNoTracking()
            .CountAsync(a => a.Action == "DiscountCommission.MarkPaid" && a.TargetId == $"SalesPartnerId={partnerId}" && a.Success))
            .Should().Be(1);
    }

    [Fact]
    public async Task Tenant_Should_Apply_Code_Before_First_Payment_Only()
    {
        var platform = await ClientAsync(platformAdmin: true);
        var partnerId = await CreatePartnerAsync(platform, "Sonradan Ortak");
        var code = UniqueCode();
        await CreateCodeAsync(platform, partnerId, code, 10m, 20m);

        var signup = await SignupAsync(null);
        signup.Response.StatusCode.Should().Be(HttpStatusCode.OK, await signup.Response.Content.ReadAsStringAsync());
        var tenantId = (await signup.Response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        var owner = _factory.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, signup.Email,
                [PermissionCatalog.Subscriptions.Read, PermissionCatalog.Subscriptions.Manage]));

        var before = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/subscription-discount");
        before.GetProperty("applied").GetBoolean().Should().BeFalse();
        before.GetProperty("canApply").GetBoolean().Should().BeTrue();

        var invalid = await owner.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/subscription-discount", new { code = "YANLIS-KOD" });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(invalid)).Should().Be("DiscountCodes.Invalid");

        var applied = await owner.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/subscription-discount", new { code });
        applied.StatusCode.Should().Be(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        (await applied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applied").GetBoolean().Should().BeTrue();

        // İlk ücretli ödemeden sonra kod girilemez (başka kiracı ile).
        var paidSignup = await SignupAsync(null);
        var paidTenantId = (await paidSignup.Response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        await SimulateCollectionAsync(paidTenantId, 99_900, 0, BillingActivationSource.Webhook);
        var paidOwner = _factory.CreateClient();
        paidOwner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, paidSignup.Email,
                [PermissionCatalog.Subscriptions.Read, PermissionCatalog.Subscriptions.Manage]));
        var late = await paidOwner.PostAsJsonAsync($"/api/v1/tenants/{paidTenantId}/subscription-discount", new { code });
        late.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(late)).Should().Be("DiscountCodes.ApplyNotOpen");
    }

    [Theory]
    [InlineData(BillingProvider.Iyzico, HttpStatusCode.Conflict, "Billing.ManualFinalizeNotAllowed")]
    [InlineData(BillingProvider.Manual, HttpStatusCode.OK, null)]
    public async Task Panel_Finalize_Should_Activate_Only_Manual_Provider_Sessions(
        BillingProvider provider, HttpStatusCode expectedStatus, string? expectedCode)
    {
        var signup = await SignupAsync(null);
        var tenantId = (await signup.Response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();

        Guid sessionId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = BillingCheckoutSession.CreatePending(
                tenantId, SubscriptionPlanCode.Basic, SubscriptionPlanCode.Pro, provider, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30));
            db.BillingCheckoutSessions.Add(session);
            await db.SaveChangesAsync();
            sessionId = session.Id;
        }

        var owner = _factory.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, signup.Email,
                [PermissionCatalog.Subscriptions.Read, PermissionCatalog.Subscriptions.Manage]));
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/subscription-checkout/{sessionId}/finalize", new { externalReference = "panel-ref" });

        response.StatusCode.Should().Be(expectedStatus, await response.Content.ReadAsStringAsync());
        if (expectedCode is not null)
            (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be(expectedCode);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = await verifyDb.TenantSubscriptions.SingleAsync(s => s.TenantId == tenantId);
        if (provider == BillingProvider.Manual)
            sub.PlanCode.Should().Be(SubscriptionPlanCode.Pro);
        else
            sub.PlanCode.Should().Be(SubscriptionPlanCode.Basic, "sağlayıcıdan doğrulanmamış ödeme planı değiştirmemeli");
    }

    // ---- Kurulum ----

    private static string UniqueCode() => $"IT{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private async Task<HttpClient> ClientAsync(bool platformAdmin)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = await db.Users.SingleAsync(u => u.Email == AdminEmail);
        var tenantId = await db.UserTenants.Where(ut => ut.UserId == user.Id).Select(ut => ut.TenantId).FirstAsync();

        var claims = PermissionCatalog.AllCodes.Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenantId.ToString("D")))
            .ToList();
        if (platformAdmin)
            claims.Add(new Claim(VeterinerClaims.PlatformAdmin, bool.TrueString));

        var (token, _, _) = jwt.Create(user.Id, user.Email, Array.Empty<string>(), claims);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<Guid> CreatePartnerAsync(HttpClient platform, string name, string? email = null)
    {
        var response = await platform.PostAsJsonAsync(Base, new { name, email, phone = "05550000000" });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateCodeAsync(HttpClient platform, Guid partnerId, string code, decimal discount, decimal commission)
    {
        var response = await platform.PostAsJsonAsync($"{Base}/{partnerId}/codes",
            new { code, discountPercent = discount, commissionPercent = commission });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ValidateAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/v1/public/discount-codes/validate", new { code });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(HttpResponseMessage Response, string Email)> SignupAsync(string? discountCode, string? email = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        email ??= $"ortak-kayit-{suffix}@example.com";
        var documents = LegalDocumentCatalog.CurrentVersions
            .Select(kv => (object)new { documentType = kv.Key.ToString(), version = kv.Value })
            .ToArray();

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/public/owner-signup", new
        {
            planCode = "Basic",
            tenantName = $"Ortak Kiracı {suffix}",
            clinicName = "Ortak Klinik",
            clinicCity = "İzmir",
            email,
            password = "Abc12345!",
            acceptedLegalDocuments = documents,
            discountCode,
        });
        return (response, email);
    }

    private async Task<Guid> SimulateCollectionAsync(Guid tenantId, long grossMinor, long discountMinor, BillingActivationSource source)
    {
        Guid sessionId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = BillingCheckoutSession.CreatePending(
                tenantId, SubscriptionPlanCode.Basic, SubscriptionPlanCode.Basic,
                source == BillingActivationSource.Manual ? BillingProvider.Manual : BillingProvider.Iyzico,
                DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30));
            session.RecordPricing(grossMinor, discountMinor);
            db.BillingCheckoutSessions.Add(session);
            await db.SaveChangesAsync();
            sessionId = session.Id;
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var activation = scope.ServiceProvider.GetRequiredService<ISubscriptionCheckoutActivationService>();
            var result = await activation.TryActivateAsync(sessionId, null, null, "pay-sim", source, CancellationToken.None);
            result.IsSuccess.Should().BeTrue(result.IsSuccess ? null : result.Error.Message);
        }

        return sessionId;
    }
}
