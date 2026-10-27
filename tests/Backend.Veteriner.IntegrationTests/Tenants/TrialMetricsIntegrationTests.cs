using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Tenants;

/// <summary>
/// TRIAL-008 uçtan uca (HTTP + LocalDB): kayıt → (örnek klinik) → ilk komut akışında <c>TenantActivations</c> satırı
/// (örnek klinik işareti, ilk aksiyon bir kez), platform yöneticisi özeti (200) ve kohort sayıları, klinik yöneticisi 403,
/// geçersiz dönem 400. Bkz. docs/TRIAL_ACTIVATION_METRICS_API_CONTRACT.md.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class TrialMetricsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminEmail = "admin@example.com";
    private const string Base = "/api/v1/admin/trial-metrics";

    private readonly CustomWebApplicationFactory _factory;

    public TrialMetricsIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Endpoint_Should_Require_Platform_Admin_And_Validate_Range()
    {
        var clinicAdmin = await ClientAsync(platformAdmin: false);
        (await clinicAdmin.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var platform = await ClientAsync(platformAdmin: true);
        (await platform.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.OK);

        var inverted = await platform.GetAsync($"{Base}?fromUtc=2026-10-25T00:00:00Z&toUtc=2026-10-01T00:00:00Z");
        inverted.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(inverted)).Should().Be("TrialMetrics.InvalidRange");

        var tooLong = await platform.GetAsync($"{Base}?fromUtc=2024-01-01T00:00:00Z&toUtc=2026-01-01T00:00:00Z");
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Signup_ExampleClinic_And_FirstCommand_Should_Produce_Activation_Row_And_Metrics()
    {
        var windowStart = DateTime.UtcNow.AddMinutes(-1);

        // Örnek klinikle başlayan kiracı: yükleme işareti var, kullanıcı aksiyonu henüz yok.
        var (exampleResponse, exampleEmail) = await SignupAsync(startWithExampleClinic: true);
        exampleResponse.StatusCode.Should().Be(HttpStatusCode.OK, await exampleResponse.Content.ReadAsStringAsync());
        var exampleTenantId = (await exampleResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();

        var afterLoad = await ActivationAsync(exampleTenantId);
        afterLoad.Should().NotBeNull();
        afterLoad!.ExampleClinicLoadedAtUtc.Should().NotBeNull();
        afterLoad.FirstActionAtUtc.Should().BeNull("örnek klinik yüklemesi kullanıcı aksiyonu sayılmaz");

        // Boş klinikle başlayan kiracı: hiç kayıt yok.
        var (emptyResponse, emptyEmail) = await SignupAsync(startWithExampleClinic: false);
        emptyResponse.StatusCode.Should().Be(HttpStatusCode.OK, await emptyResponse.Content.ReadAsStringAsync());
        var emptyTenantId = (await emptyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        (await ActivationAsync(emptyTenantId)).Should().BeNull();

        // Örnek klinikli kiracıda ilk anlamlı aksiyon: müşteri ekleme; ikinci müşteri ilk aksiyonu değiştirmez.
        var owner = await OwnerClientAsync(exampleEmail);
        var first = await owner.PostAsJsonAsync("/api/v1/clients", new { fullName = "Aktivasyon Müşterisi" });
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var afterFirst = await ActivationAsync(exampleTenantId);
        afterFirst!.FirstActionKind.Should().Be(ActivationSignalKind.ClientCreated);
        afterFirst.FirstActionAtUtc.Should().NotBeNull();
        afterFirst.FirstActionUserId.Should().NotBeNull();

        var second = await owner.PostAsJsonAsync("/api/v1/clients", new { fullName = "İkinci Müşteri" });
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ActivationAsync(exampleTenantId))!.FirstActionAtUtc.Should().Be(afterFirst.FirstActionAtUtc);

        // Başarısız komut aksiyon yazmaz (boş klinikli kiracı: geçersiz gövde).
        var emptyOwner = await OwnerClientAsync(emptyEmail);
        var failed = await emptyOwner.PostAsJsonAsync("/api/v1/clients", new { fullName = "" });
        failed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ActivationAsync(emptyTenantId)).Should().BeNull();

        // Platform özeti: dönem iki kiracıyı kapsar; paralel testler kohortu büyütebilir, alt sınırlar doğrulanır.
        var platform = await ClientAsync(platformAdmin: true);
        var to = DateTime.UtcNow.AddMinutes(1);
        var metrics = await platform.GetFromJsonAsync<JsonElement>($"{Base}?fromUtc={windowStart:O}&toUtc={to:O}");

        metrics.GetProperty("trialStarts").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        metrics.GetProperty("exampleClinic").GetProperty("count").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        metrics.GetProperty("activation").GetProperty("count").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        metrics.GetProperty("activation").GetProperty("within10MinutesCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        metrics.GetProperty("activation").GetProperty("medianMinutesToFirstAction").GetDecimal().Should().BeGreaterThanOrEqualTo(0m);
        metrics.GetProperty("conversion").GetProperty("count").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        metrics.GetProperty("firstActionBreakdown").EnumerateArray()
            .Should().Contain(b => b.GetProperty("kind").GetString() == "ClientCreated");
        metrics.GetProperty("byOnboarding").EnumerateArray().Select(b => b.GetProperty("onboarding").GetString())
            .Should().Equal("ExampleClinic", "EmptyClinic");
        metrics.GetProperty("byOnboarding").EnumerateArray().First().GetProperty("trialStarts").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        // Dönem dışı pencere kohortu içermez.
        var past = await platform.GetFromJsonAsync<JsonElement>($"{Base}?fromUtc=2020-01-01T00:00:00Z&toUtc=2020-02-01T00:00:00Z");
        past.GetProperty("trialStarts").GetInt32().Should().Be(0);
        past.GetProperty("activation").GetProperty("rate").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private async Task<TenantActivation?> ActivationAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.TenantActivations.AsNoTracking().SingleOrDefaultAsync(a => a.TenantId == tenantId);
    }

    private async Task<HttpClient> OwnerClientAsync(string email)
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, [PermissionCatalog.Clients.Create]));
        return http;
    }

    private async Task<HttpClient> ClientAsync(bool platformAdmin)
    {
        await using var scope = _factory.Services.CreateSystemScope();
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

    private async Task<(HttpResponseMessage Response, string Email)> SignupAsync(bool startWithExampleClinic)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"trial-olcum-{suffix}@example.com";
        var documents = LegalDocumentCatalog.CurrentVersions
            .Select(kv => (object)new { documentType = kv.Key.ToString(), version = kv.Value })
            .ToArray();

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/public/owner-signup", new
        {
            planCode = "Basic",
            tenantName = $"Ölçüm Kiracı {suffix}",
            clinicName = "Ölçüm Kliniği",
            clinicCity = "Ankara",
            email,
            password = "Abc12345!",
            acceptedLegalDocuments = documents,
            startWithExampleClinic,
        });
        return (response, email);
    }
}
