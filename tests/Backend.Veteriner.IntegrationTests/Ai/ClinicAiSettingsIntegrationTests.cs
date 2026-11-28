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
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
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
/// Klinik ayarlarında yapay zekâ (ADR-044 E1, TIER-013 Dilim 1): klinik yöneticisi bilgilendirmeyi kabul ederek açar, kapatır;
/// okuma <c>Clinics.Read</c>, yazma <c>Clinics.Update</c>; yanıtta dolar ve platform iç notu yok. HTTP + LocalDB, sağlayıcı sahte.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ClinicAiSettingsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] ManagerPermissions =
        [PermissionCatalog.Clinics.Read, PermissionCatalog.Clinics.Update, PermissionCatalog.Ai.Use];

    private static readonly string[] ReaderPermissions = [PermissionCatalog.Clinics.Read];

    private readonly WebApplicationFactory<global::Program> _factory;

    public ClinicAiSettingsIntegrationTests(CustomWebApplicationFactory factory)
        => _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiTextGenerator>();
            services.AddSingleton<IAiTextGenerator, ConfiguredAiTextGenerator>();
        }));

    [Fact]
    public async Task Clinic_Manager_Enables_After_Acknowledging_Disables_And_Reenables_Without_Asking_Again()
    {
        var ctx = await SeedAsync(ManagerPermissions);

        var initial = await JsonAsync(await ctx.Http.GetAsync(ctx.Url), HttpStatusCode.OK);
        initial.GetProperty("isEnabled").GetBoolean().Should().BeFalse();
        initial.GetProperty("isConfigured").GetBoolean().Should().BeTrue();
        initial.GetProperty("unavailableCode").GetString().Should().Be("Ai.ClinicNotEnabled");
        initial.GetProperty("hasAcknowledged").GetBoolean().Should().BeFalse();
        initial.GetProperty("monthlyCredits").GetInt32().Should().Be(2_000, "varsayılan kiracı Premium");
        var acknowledgement = initial.GetProperty("acknowledgement");
        acknowledgement.GetProperty("version").GetInt32().Should().Be(AiAcknowledgement.Version);
        acknowledgement.GetProperty("items").GetArrayLength().Should().Be(AiAcknowledgement.Items.Count);

        await ProblemAsync(await ctx.Http.PutAsJsonAsync(ctx.Url, new { isEnabled = true }), HttpStatusCode.BadRequest, "Ai.AcknowledgementRequired");
        await ProblemAsync(
            await ctx.Http.PutAsJsonAsync(ctx.Url, new { isEnabled = true, acknowledgementVersion = AiAcknowledgement.Version - 1 }),
            HttpStatusCode.BadRequest, "Ai.AcknowledgementRequired");

        var enabled = await JsonAsync(
            await ctx.Http.PutAsJsonAsync(ctx.Url, new { isEnabled = true, acknowledgementVersion = AiAcknowledgement.Version }),
            HttpStatusCode.OK);
        enabled.GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        enabled.GetProperty("isAvailable").GetBoolean().Should().BeTrue();
        enabled.GetProperty("hasAcknowledged").GetBoolean().Should().BeTrue();
        enabled.GetProperty("acknowledgedAtUtc").ValueKind.Should().Be(JsonValueKind.String);

        var status = await JsonAsync(await ctx.Http.GetAsync("/api/v1/ai/status"), HttpStatusCode.OK);
        status.GetProperty("isClinicEnabled").GetBoolean().Should().BeTrue();

        var disabled = await JsonAsync(await ctx.Http.PutAsJsonAsync(ctx.Url, new { isEnabled = false }), HttpStatusCode.OK);
        disabled.GetProperty("isEnabled").GetBoolean().Should().BeFalse();
        disabled.GetProperty("hasAcknowledged").GetBoolean().Should().BeTrue("kapatmak kabulü silmez");

        var reenabled = await JsonAsync(await ctx.Http.PutAsJsonAsync(ctx.Url, new { isEnabled = true }), HttpStatusCode.OK);
        reenabled.GetProperty("isEnabled").GetBoolean().Should().BeTrue("güncel sürüm daha önce kabul edildi");

        var row = await ctx.SettingsAsync();
        row.AcknowledgedVersion.Should().Be(AiAcknowledgement.Version);
        row.AcknowledgedByUserId.Should().Be(ctx.UserId);
        row.UpdatedByUserId.Should().Be(ctx.UserId);
    }

    [Fact]
    public async Task Clinic_Sees_Credit_Grants_Without_Platform_Note_Or_Dollars()
    {
        var ctx = await SeedAsync(ManagerPermissions);
        var admin = await PlatformAdminClientAsync();
        await JsonAsync(
            await admin.PostAsJsonAsync($"/api/v1/admin/ai/clinics/{ctx.ClinicId}/fund-grants", new { credits = 250, note = "GizliIcNot" }),
            HttpStatusCode.OK);

        var response = await ctx.Http.GetAsync(ctx.Url);
        var body = await JsonAsync(response, HttpStatusCode.OK);
        body.GetProperty("fundCredits").GetDecimal().Should().Be(250m);
        body.GetProperty("creditsRemaining").GetDecimal().Should().Be(2_250m);
        var grant = body.GetProperty("creditGrants").EnumerateArray().Should().ContainSingle().Which;
        grant.GetProperty("credits").GetInt32().Should().Be(250);
        grant.GetProperty("kind").GetString().Should().Be("Manual");
        var raw = body.GetRawText();
        raw.Should().NotContain("GizliIcNot").And.NotContain("Usd").And.NotContain("createdByUserId");
    }

    [Fact]
    public async Task Reader_Cannot_Toggle_And_Unknown_Clinic_Is_Not_Found()
    {
        var ctx = await SeedAsync(ReaderPermissions);

        (await ctx.Http.GetAsync(ctx.Url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ctx.Http.PutAsJsonAsync(ctx.Url, new { isEnabled = true, acknowledgementVersion = AiAcknowledgement.Version }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ctx.Http.GetAsync($"/api/v1/clinics/{Guid.NewGuid()}/ai-settings")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await ctx.SettingsOrNullAsync()).Should().BeNull("reddedilen istek kayıt açmaz");
    }

    [Fact]
    public async Task Platform_Admin_Enable_Does_Not_Count_As_Clinic_Acknowledgement()
    {
        var ctx = await SeedAsync(ManagerPermissions);
        var admin = await PlatformAdminClientAsync();
        await JsonAsync(await admin.PutAsJsonAsync($"/api/v1/admin/ai/clinics/{ctx.ClinicId}", new { isEnabled = true }), HttpStatusCode.OK);

        var body = await JsonAsync(await ctx.Http.GetAsync(ctx.Url), HttpStatusCode.OK);
        body.GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        body.GetProperty("hasAcknowledged").GetBoolean().Should().BeFalse();

        // Açık kliniği kapatmak kabul istemez; yeniden açmak ister.
        await JsonAsync(await ctx.Http.PutAsJsonAsync(ctx.Url, new { isEnabled = false }), HttpStatusCode.OK);
        await ProblemAsync(await ctx.Http.PutAsJsonAsync(ctx.Url, new { isEnabled = true }), HttpStatusCode.BadRequest, "Ai.AcknowledgementRequired");
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
            $"ai-ayar-{Guid.NewGuid():N}@example.com",
            Array.Empty<string>(),
            [new Claim(VeterinerClaims.PlatformAdmin, bool.TrueString)]);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private async Task<SettingsTestContext> SeedAsync(string[] permissions)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid clinicId, userId;
        string email;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"AiAyar{Guid.NewGuid():N}"[..16], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"ai-ayar-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id, DateTime.UtcNow));
            await db.SaveChangesAsync();
            (clinicId, userId) = (clinic.Id, user.Id);
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, permissions, clinicId);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new SettingsTestContext(_factory.Services, http, clinicId, userId);
    }

    private sealed record SettingsTestContext(IServiceProvider Services, HttpClient Http, Guid ClinicId, Guid UserId)
    {
        public string Url => $"/api/v1/clinics/{ClinicId}/ai-settings";

        public async Task<ClinicAiSettings> SettingsAsync() => (await SettingsOrNullAsync())!;

        public async Task<ClinicAiSettings?> SettingsOrNullAsync()
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<ClinicAiSettings>().AsNoTracking().SingleOrDefaultAsync(s => s.ClinicId == ClinicId);
        }
    }

    /// <summary>Anahtarı girilmiş sağlayıcı yerine geçer; bu testler sağlayıcı çağırmaz.</summary>
    private sealed class ConfiguredAiTextGenerator : IAiTextGenerator
    {
        public bool IsConfigured => true;

        public Task<AiGenerationResult> GenerateJsonAsync(AiGenerationRequest request, CancellationToken ct)
            => Task.FromResult(new AiGenerationResult(AiGenerationStatus.Completed, """{"sections":[]}""", 100, 10));
    }
}
