using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Ai;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Ai;

/// <summary>
/// AI-007 kredi kotası ve AI-008 maliyet takibi (ADR-044): HTTP + gerçek SQL Server (LocalDB), sağlayıcı sahte. Kredi harcaması
/// (önce kota, sonra fon), durum ucu, %80 uyarısı (ayda bir), kota bitince yalnız AI'nin durması, platform yöneticisinin klinik
/// listesi / fon ekleme / aylık kırılımlar. Her test kendi kliniğini kurar (varsayılan kiracı: Premium, aylık 2.000 kredi).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class AiQuotaIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] Permissions =
    [
        PermissionCatalog.Ai.Use,
        PermissionCatalog.Pets.Read,
        PermissionCatalog.Examinations.Read,
    ];

    private readonly WebApplicationFactory<global::Program> _factory;
    private readonly FakeAiTextGenerator _ai = new();

    public AiQuotaIntegrationTests(CustomWebApplicationFactory factory)
        => _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiTextGenerator>();
            services.AddSingleton<IAiTextGenerator>(_ai);
        }));

    [Fact]
    public async Task Credits_Come_From_Monthly_Quota_Then_Fund_Warn_Once_And_Stop_Only_Ai()
    {
        var ctx = await SeedAsync(aiEnabled: true);
        var summaryUrl = $"/api/v1/pets/{ctx.PetId}/ai/history-summary";

        var fresh = await JsonAsync(await ctx.Http.GetAsync("/api/v1/ai/status"), HttpStatusCode.OK);
        fresh.GetProperty("monthlyCredits").GetInt32().Should().Be(2_000, "varsayılan kiracı Premium");
        fresh.GetProperty("creditsRemaining").GetDecimal().Should().Be(2_000m);
        fresh.GetProperty("clinicMonthUsedPercent").GetInt32().Should().Be(0);
        var (_, monthEnd) = OperationPeriodBounds.MonthForUtcNow(DateTime.UtcNow);
        fresh.GetProperty("quotaResetsAtUtc").GetDateTime().Should().Be(monthEnd);

        // Kotanın 0,5 kredisi kalsın; platform yöneticisi 10 kredi fon ekler.
        await ctx.AddUsageAsync(credits: 1_999.5m, fundCredits: 0m);
        var admin = await PlatformAdminClientAsync();
        var funded = await JsonAsync(
            await admin.PostAsJsonAsync($"/api/v1/admin/ai/clinics/{ctx.ClinicId}/fund-grants", new { credits = 10, note = "Deneme" }),
            HttpStatusCode.OK);
        funded.GetProperty("fundCredits").GetDecimal().Should().Be(10m);
        funded.GetProperty("creditsRemaining").GetDecimal().Should().Be(10.5m);

        // 1.200 + 300 token Haiku = $0,00027 = 0,6 kredi: 0,5 kotadan, 0,1 fondan.
        _ai.Respond("""{"sections":[]}""", inputTokens: 1_200, outputTokens: 300);
        await JsonAsync(await ctx.Http.PostAsync(summaryUrl, null), HttpStatusCode.OK);
        var record = (await ctx.UsageRecordsAsync()).Single(r => r.Model == "claude-haiku-5-5" && r.PromptVersion != "test/1");
        record.Credits.Should().Be(0.6m);
        record.FundCredits.Should().Be(0.1m);

        var status = await JsonAsync(await ctx.Http.GetAsync("/api/v1/ai/status"), HttpStatusCode.OK);
        status.GetProperty("isAvailable").GetBoolean().Should().BeTrue();
        status.GetProperty("fundCredits").GetDecimal().Should().Be(9.9m);
        status.GetProperty("creditsRemaining").GetDecimal().Should().Be(9.9m);
        status.GetProperty("clinicMonthUsedPercent").GetInt32().Should().Be(99);

        // %80 uyarısı klinik yöneticilerine bir kez; ikinci istek yeni uyarı üretmez.
        await JsonAsync(await ctx.Http.PostAsync(summaryUrl, null), HttpStatusCode.OK);
        var warnings = await ctx.NotificationsAsync();
        warnings.Should().NotBeEmpty().And.OnlyContain(n => n.Type == UserNotificationType.AiQuotaNearlyUsed);
        warnings.Should().Contain(n => n.UserId == ctx.UserId);
        warnings.GroupBy(n => n.UserId).Should().OnlyContain(g => g.Count() == 1);
        warnings[0].TargetType.Should().Be("AiUsage");

        // Fon da bitince yalnız AI durur.
        await ctx.AddUsageAsync(credits: 9.3m, fundCredits: 9.3m);
        var exhausted = await JsonAsync(await ctx.Http.GetAsync("/api/v1/ai/status"), HttpStatusCode.OK);
        exhausted.GetProperty("unavailableCode").GetString().Should().Be("Ai.ClinicQuotaExceeded");
        exhausted.GetProperty("clinicMonthUsedPercent").GetInt32().Should().Be(100);
        exhausted.GetProperty("creditsRemaining").GetDecimal().Should().Be(0m);
        await ProblemAsync(await ctx.Http.PostAsync(summaryUrl, null), HttpStatusCode.TooManyRequests, "Ai.ClinicQuotaExceeded");
    }

    [Fact]
    public async Task Exhausting_Credits_In_One_Request_Sends_Only_The_End_Warning()
    {
        var ctx = await SeedAsync(aiEnabled: true);
        await ctx.AddUsageAsync(credits: 1_999m, fundCredits: 0m); // 1 kredi kaldı, uyarı henüz verilmedi (doğrudan kayıt)

        _ai.Respond("""{"sections":[]}""", inputTokens: 12_000, outputTokens: 2_000); // 4,89 kredi: kalan 1'i aşar
        await JsonAsync(await ctx.Http.PostAsync($"/api/v1/pets/{ctx.PetId}/ai/history-summary", null), HttpStatusCode.OK);

        var notifications = await ctx.NotificationsAsync();
        notifications.Should().NotBeEmpty().And.OnlyContain(n => n.Type == UserNotificationType.AiQuotaExhausted);
        notifications.GroupBy(n => n.UserId).Should().OnlyContain(g => g.Count() == 1);
        notifications[0].Title.Should().Be("Yapay zekâ kredisi bitti");

        var record = (await ctx.UsageRecordsAsync()).Single(r => r.PromptVersion != "test/1");
        (record.Credits, record.FundCredits).Should().Be((4.89m, 3.89m), "aşan kısım fondan düşer, fon eksiye iner");
        var status = await JsonAsync(await ctx.Http.GetAsync("/api/v1/ai/status"), HttpStatusCode.OK);
        status.GetProperty("creditsRemaining").GetDecimal().Should().Be(0m);
        status.GetProperty("unavailableCode").GetString().Should().Be("Ai.ClinicQuotaExceeded");
    }

    [Fact]
    public async Task Platform_Admin_Lists_Searches_Funds_And_Reads_Monthly_Breakdown()
    {
        var ctx = await SeedAsync(aiEnabled: false);
        var admin = await PlatformAdminClientAsync();

        (await ctx.Http.GetAsync("/api/v1/admin/ai/clinics")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ProblemAsync(await admin.GetAsync("/api/v1/admin/ai/clinics?search=a"), HttpStatusCode.BadRequest, "Ai.Validation");

        var found = await JsonAsync(await admin.GetAsync($"/api/v1/admin/ai/clinics?search={ctx.ClinicName}"), HttpStatusCode.OK);
        var row = found.EnumerateArray().Single(c => c.GetProperty("clinicId").GetGuid() == ctx.ClinicId);
        row.GetProperty("isEnabled").GetBoolean().Should().BeFalse();
        row.GetProperty("tenantName").GetString().Should().Be(DataSeeder.DefaultTenantName);
        row.GetProperty("monthlyCredits").GetInt32().Should().Be(
            new AiOptions().MonthlyCreditsFor(row.GetProperty("planCode").GetString()));

        await ProblemAsync(
            await admin.PostAsJsonAsync($"/api/v1/admin/ai/clinics/{ctx.ClinicId}/fund-grants", new { credits = 0 }),
            HttpStatusCode.BadRequest, "Validation.FluentValidation");
        await JsonAsync(
            await admin.PostAsJsonAsync($"/api/v1/admin/ai/clinics/{ctx.ClinicId}/fund-grants", new { credits = 250, note = "  Pilot telafisi " }),
            HttpStatusCode.OK);
        var grants = await JsonAsync(await admin.GetAsync($"/api/v1/admin/ai/clinics/{ctx.ClinicId}/fund-grants"), HttpStatusCode.OK);
        var grant = grants.EnumerateArray().Should().ContainSingle().Which;
        grant.GetProperty("credits").GetInt32().Should().Be(250);
        grant.GetProperty("note").GetString().Should().Be("Pilot telafisi");
        grant.GetProperty("kind").GetString().Should().Be("Manual");

        await JsonAsync(await admin.PutAsJsonAsync($"/api/v1/admin/ai/clinics/{ctx.ClinicId}", new { isEnabled = true }), HttpStatusCode.OK);
        var listed = await JsonAsync(await admin.GetAsync("/api/v1/admin/ai/clinics"), HttpStatusCode.OK);
        listed.EnumerateArray().Should().Contain(c => c.GetProperty("clinicId").GetGuid() == ctx.ClinicId
            && c.GetProperty("isEnabled").GetBoolean()
            && c.GetProperty("fundCredits").GetDecimal() == 250m);

        _ai.Respond("""{"sections":[]}""", inputTokens: 1_000, outputTokens: 100);
        await JsonAsync(await ctx.Http.PostAsync($"/api/v1/pets/{ctx.PetId}/ai/history-summary", null), HttpStatusCode.OK);

        var usage = await JsonAsync(await admin.GetAsync("/api/v1/admin/ai/usage"), HttpStatusCode.OK);
        usage.GetProperty("isCurrentMonth").GetBoolean().Should().BeTrue();
        usage.GetProperty("month").GetString().Should().Be(OperationDayBounds.ToLocal(DateTime.UtcNow).ToString("yyyy-MM"));
        usage.GetProperty("budget").GetProperty("limitUsd").GetDecimal().Should().Be(10m);
        usage.GetProperty("limits").GetProperty("planMonthlyCredits").GetProperty("Premium").GetInt32().Should().Be(2_000);
        var clinicUsage = usage.GetProperty("clinics").EnumerateArray().Single(c => c.GetProperty("clinicId").GetGuid() == ctx.ClinicId).GetProperty("usage");
        clinicUsage.GetProperty("requests").GetInt32().Should().Be(1);
        clinicUsage.GetProperty("inputTokens").GetInt64().Should().Be(1_000);
        clinicUsage.GetProperty("outputTokens").GetInt64().Should().Be(100);
        clinicUsage.GetProperty("credits").GetDecimal().Should().Be(0.33m);
        clinicUsage.GetProperty("costUsd").GetDecimal().Should().Be(0.00015m);
        usage.GetProperty("features").EnumerateArray().Should().Contain(f => f.GetProperty("feature").GetString() == "PetHistorySummary");
        usage.GetProperty("models").EnumerateArray().Should().Contain(m => m.GetProperty("model").GetString() == "claude-haiku-5-5");
        usage.GetRawText().Should().NotContain("sections").And.NotContain(ctx.PetName, "klinik metin dönmez");

        var past = await JsonAsync(await admin.GetAsync("/api/v1/admin/ai/usage?month=2020-01"), HttpStatusCode.OK);
        past.GetProperty("isCurrentMonth").GetBoolean().Should().BeFalse();
        past.GetProperty("system").GetProperty("requests").GetInt32().Should().Be(0);
        past.GetProperty("clinics").GetArrayLength().Should().Be(0);
        (await admin.GetAsync("/api/v1/admin/ai/usage?month=2026-13")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode expected, string code)
        => (await JsonAsync(response, expected)).GetProperty("code").GetString().Should().Be(code);

    private async Task<HttpClient> PlatformAdminClientAsync()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _, _) = jwt.Create(
            Guid.NewGuid(),
            $"ai-kota-{Guid.NewGuid():N}@example.com",
            Array.Empty<string>(),
            [new Claim(VeterinerClaims.PlatformAdmin, bool.TrueString)]);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private async Task<QuotaTestContext> SeedAsync(bool aiEnabled)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId, clinicId, petId, userId;
        string email, clinicName, petName;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            clinicName = $"AiKota{Guid.NewGuid():N}"[..16];
            var clinic = new Clinic(tenant.Id, clinicName, "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"ai-kota-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id, DateTime.UtcNow));

            var client = new Client(tenant.Id, "Kota Sahibi", DateTime.UtcNow, "905320000000");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            petName = $"Kota{Guid.NewGuid():N}"[..12];
            var pet = new Pet(tenant.Id, client.Id, petName, speciesId);
            db.Pets.Add(pet);
            db.Examinations.Add(new Examination(tenant.Id, clinic.Id, pet.Id, null, DateTime.UtcNow.AddDays(-3), "Kontrol", "Normal", null, null, DateTime.UtcNow));

            if (aiEnabled)
            {
                var settings = ClinicAiSettings.Create(tenant.Id, clinic.Id, DateTime.UtcNow);
                settings.SetEnabled(true, null, DateTime.UtcNow);
                db.Add(settings);
            }

            await db.SaveChangesAsync();
            (tenantId, clinicId, petId, userId) = (tenant.Id, clinic.Id, pet.Id, user.Id);
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, Permissions, clinicId);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new QuotaTestContext(_factory.Services, http, tenantId, clinicId, clinicName, petId, petName, userId);
    }

    private sealed record QuotaTestContext(
        IServiceProvider Services, HttpClient Http, Guid TenantId, Guid ClinicId, string ClinicName, Guid PetId, string PetName, Guid UserId)
    {
        /// <summary>Doğrudan kullanım kaydı (önceki istekler yerine); maliyet sigorta sınırlarından uzak tutulur.</summary>
        public async Task AddUsageAsync(decimal credits, decimal fundCredits)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(AiUsageRecord.Create(
                TenantId, ClinicId, Guid.NewGuid(), AiFeature.PetHistorySummary, PetId, null, null, "claude-haiku-5-5", "test/1",
                1_000, 100, 0.0001m, credits, fundCredits, 500, AiUsageOutcome.Succeeded, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<AiUsageRecord>> UsageRecordsAsync()
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.AiUsageRecords.AsNoTracking().Where(r => r.ClinicId == ClinicId).ToListAsync();
        }

        public async Task<IReadOnlyList<UserNotification>> NotificationsAsync()
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.UserNotifications.AsNoTracking()
                .Where(n => n.ClinicId == ClinicId
                    && (n.Type == UserNotificationType.AiQuotaNearlyUsed || n.Type == UserNotificationType.AiQuotaExhausted))
                .ToListAsync();
        }
    }

    /// <summary>Sahte sağlayıcı: ayarlanan yanıtı döner. Sınıf içi testler sırayla çalışır.</summary>
    private sealed class FakeAiTextGenerator : IAiTextGenerator
    {
        private AiGenerationResult _next = new(AiGenerationStatus.Completed, """{"sections":[]}""", 100, 10);

        public bool IsConfigured => true;

        public void Respond(string text, int inputTokens, int outputTokens)
            => _next = new AiGenerationResult(AiGenerationStatus.Completed, text, inputTokens, outputTokens);

        public Task<AiGenerationResult> GenerateJsonAsync(AiGenerationRequest request, CancellationToken ct) => Task.FromResult(_next);
    }
}
