using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Legal;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auth;

/// <summary>Sürümlü hukuki kabul uçları ve kayıt akışında sunucu doğrulaması (ADR-029).</summary>
public sealed class LegalAcceptanceEndpointIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LegalAcceptanceEndpointIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static object[] AllCurrentDocuments() =>
        LegalDocumentCatalog.CurrentVersions
            .Select(kv => (object)new { documentType = kv.Key.ToString(), version = kv.Value })
            .ToArray();

    private static string[] PendingTypes(JsonElement json) =>
        json.GetProperty("pendingDocuments").EnumerateArray()
            .Select(d => d.GetProperty("documentType").GetString()!)
            .ToArray();

    private async Task<HttpResponseMessage> GetStatusAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/legal-acceptances");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> AcceptAsync(string token, object[] documents)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/legal-acceptances")
        {
            Content = JsonContent.Create(new { documents })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<int> CountAcceptancesAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        return await db.LegalAcceptances.CountAsync(a => a.UserId == userId);
    }

    [Fact]
    public async Task PublicLegalDocuments_Should_Return_Current_Versions_Anonymously()
    {
        var response = await _client.GetAsync("/api/v1/public/legal-documents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.EnumerateArray()
            .Select(d => (d.GetProperty("documentType").GetString(), d.GetProperty("version").GetString()))
            .Should().BeEquivalentTo(LegalDocumentCatalog.StaffDocuments.Select(t => (t.ToString(), LegalDocumentCatalog.CurrentVersion(t))),
                "portal belgeleri (ADR-031) personel listesinde yer almaz");
    }

    [Fact]
    public async Task Status_Should_Require_Authentication()
    {
        var response = await _client.GetAsync("/api/v1/me/legal-acceptances");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExistingAdmin_Should_See_All_Three_Pending_Then_Accept_Records_And_Audits()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, _, _) = await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, []);

        var before = await GetStatusAsync(token);
        before.StatusCode.Should().Be(HttpStatusCode.OK);
        var beforeJson = await before.Content.ReadFromJsonAsync<JsonElement>();
        beforeJson.GetProperty("requiresAcceptance").GetBoolean().Should().BeTrue();
        PendingTypes(beforeJson).Should().BeEquivalentTo("Terms", "Privacy", "DataProcessing");

        var outdated = await AcceptAsync(token,
        [
            new { documentType = "Terms", version = "0.0" },
            new { documentType = "Privacy", version = LegalDocumentCatalog.CurrentVersion(LegalDocumentType.Privacy) },
            new { documentType = "DataProcessing", version = LegalDocumentCatalog.CurrentVersion(LegalDocumentType.DataProcessing) },
        ]);
        outdated.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(outdated)).Should().Be("LegalAcceptances.VersionOutdated");
        (await CountAcceptancesAsync(email)).Should().Be(0);

        var accepted = await AcceptAsync(token, AllCurrentDocuments());
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requiresAcceptance").GetBoolean().Should().BeFalse();
        (await CountAcceptancesAsync(email)).Should().Be(3);

        var after = await (await GetStatusAsync(token)).Content.ReadFromJsonAsync<JsonElement>();
        after.GetProperty("requiresAcceptance").GetBoolean().Should().BeFalse();
        PendingTypes(after).Should().BeEmpty();

        // Tekrar kabul yeni satır yazmaz (bekleyen yok).
        (await AcceptAsync(token, AllCurrentDocuments())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountAcceptancesAsync(email)).Should().Be(3);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        (await db.AuditLogs.AsNoTracking().CountAsync(a =>
            a.Action == "Account.LegalAcceptance" && a.ActorUserId == userId && a.Success)).Should().BeGreaterThanOrEqualTo(1);
        (await db.AuditLogs.AsNoTracking().AnyAsync(a =>
            a.Action == "Account.LegalAcceptance" && a.ActorUserId == userId && !a.Success)).Should().BeTrue();
    }

    [Fact]
    public async Task ClinicAdmin_Should_Not_Be_Asked_For_DataProcessing()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, _, _, _) = await IntegrationTestAuthHelper.SeedClinicAdminUserAsync(_factory.Services, hasher);
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, []);

        var json = await (await GetStatusAsync(token)).Content.ReadFromJsonAsync<JsonElement>();
        PendingTypes(json).Should().BeEquivalentTo("Terms", "Privacy");

        var missing = await AcceptAsync(token, [new { documentType = "Terms", version = LegalDocumentCatalog.CurrentVersion(LegalDocumentType.Terms) }]);
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(missing)).Should().Be("LegalAcceptances.Required");

        (await AcceptAsync(token, AllCurrentDocuments())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountAcceptancesAsync(email)).Should().Be(2, "Veri İşleme Sözleşmesi yalnız klinik yöneticisi için kaydedilir");
    }

    [Fact]
    public async Task Accept_Should_Reject_Unknown_DocumentType()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, _, _, _) = await IntegrationTestAuthHelper.SeedClinicAdminUserAsync(_factory.Services, hasher);
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, []);

        var response = await AcceptAsync(token, [new { documentType = "Cookies", version = "0.1" }]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("Veteriner", new[] { "Terms", "Privacy" })]
    [InlineData("Admin", new[] { "Terms", "Privacy", "DataProcessing" })]
    public async Task InviteSignup_Should_Require_Documents_Of_Invited_Role_And_Record_Them(string claimName, string[] expectedTypes)
    {
        var rawToken = $"legal-invite-{Guid.NewGuid():N}";
        var email = $"legal-invitee-{Guid.NewGuid():N}@example.com";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokenHash = scope.ServiceProvider.GetRequiredService<ITokenHashService>();
            var now = DateTime.UtcNow;

            var tenant = new Tenant($"Davet Kiracı {Guid.NewGuid():N}"[..24], DateTime.UtcNow);
            var clinic = new Clinic(tenant.Id, "Davet Klinik", "İzmir");
            db.Tenants.Add(tenant);
            db.Clinics.Add(clinic);
            db.TenantSubscriptions.Add(TenantSubscription.StartTrial(
                tenant.Id, SubscriptionPlanCode.Premium, now, 14));
            var claim = await db.OperationClaims.SingleAsync(c => c.Name == claimName);
            db.TenantInvites.Add(TenantInvite.CreatePending(
                tenant.Id, clinic.Id, email, tokenHash.ComputeSha256(rawToken), claim.Id, now.AddDays(3), now));
            await db.SaveChangesAsync();
        }

        var detail = await (await _client.GetAsync($"/api/v1/public/invites/{rawToken}")).Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("requiredLegalDocuments").EnumerateArray()
            .Select(d => d.GetProperty("documentType").GetString())
            .Should().BeEquivalentTo(expectedTypes);

        var missing = await _client.PostAsJsonAsync($"/api/v1/public/invites/{rawToken}/signup-and-accept",
            new { password = "Abc12345!" });
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(missing)).Should().Be("LegalAcceptances.Required");

        var ok = await _client.PostAsJsonAsync($"/api/v1/public/invites/{rawToken}/signup-and-accept",
            new { password = "Abc12345!", acceptedLegalDocuments = AllCurrentDocuments() });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountAcceptancesAsync(email)).Should().Be(expectedTypes.Length);
    }

    [Fact]
    public async Task OwnerSignup_Should_Require_Current_Legal_Acceptance_And_Record_It()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"legal-owner-{suffix}@example.com";
        object Body(object[]? documents) => new
        {
            planCode = "Basic",
            tenantName = $"Hukuk Kiracı {suffix}",
            clinicName = "Hukuk Klinik",
            clinicCity = "Ankara",
            email,
            password = "Abc12345!",
            acceptedLegalDocuments = documents,
        };

        var missing = await _client.PostAsJsonAsync("/api/v1/public/owner-signup", Body(null));
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(missing)).Should().Be("LegalAcceptances.Required");

        var outdated = await _client.PostAsJsonAsync("/api/v1/public/owner-signup", Body(
        [
            new { documentType = "Terms", version = LegalDocumentCatalog.CurrentVersion(LegalDocumentType.Terms) },
            new { documentType = "Privacy", version = LegalDocumentCatalog.CurrentVersion(LegalDocumentType.Privacy) },
            new { documentType = "DataProcessing", version = "0.0" },
        ]));
        outdated.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(outdated)).Should().Be("LegalAcceptances.VersionOutdated");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.AnyAsync(u => u.Email == email)).Should().BeFalse("doğrulama hesap oluşturulmadan önce yapılır");
        }

        var ok = await _client.PostAsJsonAsync("/api/v1/public/owner-signup", Body(AllCurrentDocuments()));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountAcceptancesAsync(email)).Should().Be(3);

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, []);
        var status = await (await GetStatusAsync(token)).Content.ReadFromJsonAsync<JsonElement>();
        status.GetProperty("requiresAcceptance").GetBoolean().Should().BeFalse();
    }
}
