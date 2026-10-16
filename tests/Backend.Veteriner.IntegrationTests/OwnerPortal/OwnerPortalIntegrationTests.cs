using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Backend.Veteriner.Infrastructure.Security;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.OwnerPortal;

/// <summary>
/// Hasta sahibi portalı dilim A (ADR-031): davet → kabul → giriş akışı, personel/portal token ayrımı, deneme sınırı ve kilit,
/// KVKK Ret / hatırlatma istemeyen müşteriye davet gitmemesi, başka hesap/kiracı/kapalı bağla erişim reddi.
/// Ham davet token'ı ve kod e-postadadır; testte üreteç kaydedilerek okunur (outbox içeriği ayrıştırılmaz).
/// </summary>
public sealed class OwnerPortalIntegrationTests : IClassFixture<CustomWebApplicationFactory>, IDisposable
{
    private const string InvalidCredentials = "Portal.Auth.Unauthorized.InvalidCredentials";
    private const string Password = "Abc12345!";

    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory;
    private readonly RecordingSecretGenerator _secrets = new();
    private readonly HttpClient _client;

    public OwnerPortalIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOwnerPortalSecretGenerator>();
            services.AddSingleton<IOwnerPortalSecretGenerator>(_secrets);
        }));
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // ---------- yardımcılar ----------

    private sealed record StaffContext(string Token, Guid TenantId, Guid ClinicId);

    private sealed record PortalSession(Guid LinkId, string AccessToken, string RefreshToken, string Email, Guid ClientId);

    private static object[] PortalDocuments() =>
        LegalDocumentCatalog.ToDtos(LegalDocumentCatalog.RequiredForOwnerAccount)
            .Select(d => (object)new { documentType = d.DocumentType.ToString(), version = d.Version })
            .ToArray();

    private async Task<StaffContext> SeedStaffAsync(IReadOnlyCollection<string>? permissions = null)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, _, clinicId) = await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services, email, permissions ?? [PermissionCatalog.OwnerPortal.Manage], clinicId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = await db.Tenants.Where(t => t.Name == DataSeeder.DefaultTenantName).Select(t => t.Id).SingleAsync();
        return new StaffContext(token, tenantId, clinicId);
    }

    private async Task<Client> SeedClientAsync(Guid tenantId, string? email = null, Action<Client>? configure = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = new Client(tenantId, $"Portal Sahip {Guid.NewGuid():N}"[..20], email: email ?? $"owner-{Guid.NewGuid():N}@example.com");
        configure?.Invoke(client);
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client;
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string? bearer = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? bearer = null, object? body = null)
    {
        using var request = Request(method, url, bearer, body);
        return await _client.SendAsync(request);
    }

    private async Task<string> InviteAsync(StaffContext staff, Guid clientId)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/v1/clients/{clientId}/portal-invites", staff.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("token", out _).Should().BeFalse("ham davet token'ı personele dönmez");
        return _secrets.LastToken!;
    }

    private async Task<PortalSession> InviteAndAcceptAsync(StaffContext staff, string? password = Password, Client? client = null)
    {
        client ??= await SeedClientAsync(staff.TenantId);
        var rawToken = await InviteAsync(staff, client.Id);

        var accept = await SendAsync(HttpMethod.Post, $"/api/portal/v1/invites/{rawToken}/accept", body: new
        {
            password,
            acceptedLegalDocuments = PortalDocuments(),
        });
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        var json = await accept.Content.ReadFromJsonAsync<JsonElement>();
        return new PortalSession(
            json.GetProperty("linkId").GetGuid(),
            json.GetProperty("accessToken").GetString()!,
            json.GetProperty("refreshToken").GetString()!,
            client.Email!,
            client.Id);
    }

    private async Task<string> RequestCodeAsync(string email, string path = "/api/portal/v1/auth/code")
    {
        var before = _secrets.CodeCount;
        (await SendAsync(HttpMethod.Post, path, body: new { email })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        _secrets.CodeCount.Should().Be(before + 1, "aktif hesaba kod üretilmeli");
        return _secrets.LastCode!;
    }

    // ---------- testler ----------

    [Fact]
    public async Task Invite_Accept_SignIn_Refresh_Logout_Flow_Should_Work_End_To_End()
    {
        var staff = await SeedStaffAsync();
        var client = await SeedClientAsync(staff.TenantId);
        var rawToken = await InviteAsync(staff, client.Id);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invite = await db.OwnerPortalInvites.AsNoTracking().SingleAsync(i => i.ClientId == client.Id);
            invite.TokenHash.Should().NotBe(rawToken).And.HaveLength(64, "yalnız SHA-256 özeti saklanır");
            invite.ClinicId.Should().Be(staff.ClinicId);
        }

        var preview = await SendAsync(HttpMethod.Get, $"/api/portal/v1/invites/{rawToken}");
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await preview.Content.ReadFromJsonAsync<JsonElement>();
        previewJson.GetProperty("hasAccount").GetBoolean().Should().BeFalse();
        previewJson.GetProperty("emailMasked").GetString().Should().Contain("***@example.com");
        previewJson.GetProperty("requiredLegalDocuments").GetArrayLength().Should().Be(2);

        var missingLegal = await SendAsync(HttpMethod.Post, $"/api/portal/v1/invites/{rawToken}/accept", body: new { password = Password });
        missingLegal.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(missingLegal)).Should().Be("LegalAcceptances.Required");

        var accept = await SendAsync(HttpMethod.Post, $"/api/portal/v1/invites/{rawToken}/accept", body: new
        {
            displayName = "Ayşe",
            password = Password,
            acceptedLegalDocuments = PortalDocuments(),
        });
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        var acceptJson = await accept.Content.ReadFromJsonAsync<JsonElement>();
        var linkId = acceptJson.GetProperty("linkId").GetGuid();
        var access = acceptJson.GetProperty("accessToken").GetString()!;
        var refresh = acceptJson.GetProperty("refreshToken").GetString()!;

        (await SendAsync(HttpMethod.Post, $"/api/portal/v1/invites/{rawToken}/accept", body: new { acceptedLegalDocuments = PortalDocuments() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "davet tek kullanımlıktır");

        var me = await (await SendAsync(HttpMethod.Get, "/api/portal/v1/me", access)).Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("email").GetString().Should().Be(client.Email);
        me.GetProperty("hasPassword").GetBoolean().Should().BeTrue();
        me.GetProperty("links").EnumerateArray().Select(l => l.GetProperty("linkId").GetGuid()).Should().Equal(linkId);

        var legal = await (await SendAsync(HttpMethod.Get, "/api/portal/v1/me/legal-acceptances", access)).Content.ReadFromJsonAsync<JsonElement>();
        legal.GetProperty("requiresAcceptance").GetBoolean().Should().BeFalse();
        legal.GetProperty("pendingClinicDisclosures").GetArrayLength().Should().Be(1);

        var disclosure = await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{linkId}/clinic-disclosure", access);
        disclosure.StatusCode.Should().Be(HttpStatusCode.OK);
        var disclosureJson = await disclosure.Content.ReadFromJsonAsync<JsonElement>();
        disclosureJson.GetProperty("text").GetString().Should().NotContain("{{clinicName}}");
        (await SendAsync(HttpMethod.Post, $"/api/portal/v1/links/{linkId}/clinic-disclosure/acknowledge", access))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var legalAfter = await (await SendAsync(HttpMethod.Get, "/api/portal/v1/me/legal-acceptances", access)).Content.ReadFromJsonAsync<JsonElement>();
        legalAfter.GetProperty("pendingClinicDisclosures").GetArrayLength().Should().Be(0);

        (await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/login", body: new { email = client.Email, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var code = await RequestCodeAsync(client.Email!);
        var codeLogin = await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/code/verify", body: new { email = client.Email, oneTimeCode = code });
        codeLogin.StatusCode.Should().Be(HttpStatusCode.OK);

        var rotated = await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/refresh", body: new { refreshToken = refresh });
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var newRefresh = (await rotated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!;

        var reuse = await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/refresh", body: new { refreshToken = refresh });
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await IntegrationTestProblemDetails.ReadCodeAsync(reuse)).Should().Be("Portal.Auth.Unauthorized.RefreshTokenReused");
        (await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/refresh", body: new { refreshToken = newRefresh }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "tekrar kullanım tüm oturumları kapatır");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var account = await db.OwnerAccounts.AsNoTracking().SingleAsync(a => a.Email == client.Email);
            (await db.LegalAcceptances.AsNoTracking().CountAsync(a => a.OwnerAccountId == account.Id && a.UserId == null && a.TenantId == null))
                .Should().Be(2);
            (await db.AuditLogs.AsNoTracking().AnyAsync(a => a.Action == "OwnerPortal.Invite.Create" && a.TargetId == $"ClientId={client.Id}" && a.Success))
                .Should().BeTrue();
            (await db.AuditLogs.AsNoTracking().AnyAsync(a => a.Action == "Portal.ClinicDisclosure.Acknowledge" && a.Success && a.ActorUserId == null && a.TargetId!.StartsWith($"OwnerAccountId={account.Id}")))
                .Should().BeTrue();
            var codeAudit = await db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "Portal.Auth.CodeSignIn" && a.Success)
                .OrderByDescending(a => a.OccurredAtUtc)
                .FirstAsync();
            codeAudit.RequestPayload.Should().NotContain(code, "kod denetim kaydında maskelenir");
        }

        var staffView = await (await SendAsync(HttpMethod.Get, $"/api/v1/clients/{client.Id}/portal-access", staff.Token)).Content.ReadFromJsonAsync<JsonElement>();
        staffView.GetProperty("canInvite").GetBoolean().Should().BeFalse();
        staffView.GetProperty("inviteBlockedCode").GetString().Should().Be("OwnerPortal.AlreadyLinked");
        staffView.GetProperty("links")[0].GetProperty("status").GetString().Should().Be("Active");
        staffView.GetProperty("invites")[0].GetProperty("status").GetString().Should().Be("Accepted");
    }

    [Fact]
    public async Task Staff_And_Portal_Tokens_Should_Be_Rejected_On_Each_Others_Surface()
    {
        var staff = await SeedStaffAsync([PermissionCatalog.OwnerPortal.Manage, PermissionCatalog.Clients.Read]);
        var portal = await InviteAndAcceptAsync(staff);

        (await SendAsync(HttpMethod.Get, "/api/portal/v1/me", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{portal.LinkId}/clinic-disclosure", staff.Token))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await SendAsync(HttpMethod.Get, "/api/v1/clients", portal.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Get, "/api/v1/me/legal-acceptances", portal.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Get, $"/api/v1/clients/{portal.ClientId}/portal-access", portal.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Personel refresh token'ı portal yenilemesinde, portal refresh'i personel yenilemesinde geçmez (ayrı tablo).
        (await SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", body: new { refreshToken = portal.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, "/api/portal/v1/me", portal.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invite_Should_Not_Be_Sent_To_Kvkk_Declined_Or_Reminder_Opted_Out_Client()
    {
        var staff = await SeedStaffAsync();
        var declined = await SeedClientAsync(staff.TenantId, configure: c =>
            c.RecordKvkkConsent(KvkkConsentStatus.Declined, null, new DateOnly(2026, 10, 16), Guid.NewGuid(), null, null));
        var optedOut = await SeedClientAsync(staff.TenantId, configure: c => c.ApplyContactPreferences(null, remindersOptedOut: true));
        var noEmail = await SeedClientAsync(staff.TenantId, configure: c => c.UpdateDetails(c.FullName, null, null, null));
        var tokensBefore = _secrets.TokenCount;

        foreach (var (client, code) in new[]
                 {
                     (declined, "OwnerPortal.ClientOptedOut"),
                     (optedOut, "OwnerPortal.ClientOptedOut"),
                     (noEmail, "OwnerPortal.ClientEmailMissing"),
                 })
        {
            var response = await SendAsync(HttpMethod.Post, $"/api/v1/clients/{client.Id}/portal-invites", staff.Token);
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be(code);

            var access = await (await SendAsync(HttpMethod.Get, $"/api/v1/clients/{client.Id}/portal-access", staff.Token)).Content.ReadFromJsonAsync<JsonElement>();
            access.GetProperty("canInvite").GetBoolean().Should().BeFalse();
            access.GetProperty("inviteBlockedCode").GetString().Should().Be(code);
        }

        _secrets.TokenCount.Should().Be(tokensBefore, "davet token'ı üretilmez");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.OwnerPortalInvites.AnyAsync(i => i.ClientId == declined.Id || i.ClientId == optedOut.Id || i.ClientId == noEmail.Id))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Pending_Invite_Should_Fail_When_Client_Later_Opts_Out_Or_Changes_Email()
    {
        var staff = await SeedStaffAsync();
        var client = await SeedClientAsync(staff.TenantId);
        var rawToken = await InviteAsync(staff, client.Id);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracked = await db.Clients.SingleAsync(c => c.Id == client.Id);
            tracked.UpdateDetails(tracked.FullName, $"changed-{Guid.NewGuid():N}@example.com", null, null);
            await db.SaveChangesAsync();
        }

        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/invites/{rawToken}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Code_Attempt_Limit_Should_Lock_Account_With_Same_Response()
    {
        var staff = await SeedStaffAsync();
        var portal = await InviteAndAcceptAsync(staff, password: null);
        var code = await RequestCodeAsync(portal.Email);

        for (var i = 0; i < OwnerAccount.MaxFailedSignInAttempts; i++)
        {
            var wrong = await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/code/verify", body: new { email = portal.Email, oneTimeCode = "000000" == code ? "111111" : "000000" });
            wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await IntegrationTestProblemDetails.ReadCodeAsync(wrong)).Should().Be(InvalidCredentials);
        }

        var locked = await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/code/verify", body: new { email = portal.Email, oneTimeCode = code });
        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await IntegrationTestProblemDetails.ReadCodeAsync(locked)).Should().Be(InvalidCredentials, "kilit de aynı yanıtı verir");

        var unknown = await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/code/verify", body: new { email = $"nobody-{Guid.NewGuid():N}@example.com", oneTimeCode = "123456" });
        (await IntegrationTestProblemDetails.ReadCodeAsync(unknown)).Should().Be(InvalidCredentials);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.OwnerAccounts.AsNoTracking().SingleAsync(a => a.Email == portal.Email);
        account.LockedUntilUtc.Should().NotBeNull();
        (await db.OwnerPortalCodes.AsNoTracking().Where(c => c.OwnerAccountId == account.Id).SingleAsync())
            .InvalidatedAtUtc.Should().NotBeNull("5 yanlış denemede kod geçersiz olur");
    }

    [Fact]
    public async Task Unknown_Email_Should_Get_Same_Responses_As_Existing_Account()
    {
        var email = $"nobody-{Guid.NewGuid():N}@example.com";
        var codesBefore = _secrets.CodeCount;

        (await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/code", body: new { email })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/password-reset", body: new { email })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var login = await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/login", body: new { email, password = Password });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await IntegrationTestProblemDetails.ReadCodeAsync(login)).Should().Be(InvalidCredentials);
        _secrets.CodeCount.Should().Be(codesBefore);
    }

    [Fact]
    public async Task Password_Reset_With_Code_Should_Set_Password_And_Revoke_Sessions()
    {
        var staff = await SeedStaffAsync();
        var portal = await InviteAndAcceptAsync(staff, password: null);
        var code = await RequestCodeAsync(portal.Email, "/api/portal/v1/auth/password-reset");

        var weak = await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/password-reset/confirm", body: new { email = portal.Email, oneTimeCode = code, newPassword = "zayif" });
        weak.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/password-reset/confirm", body: new { email = portal.Email, oneTimeCode = code, newPassword = "Yeni12345!" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/refresh", body: new { refreshToken = portal.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/login", body: new { email = portal.Email, password = "Yeni12345!" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Link_Of_Another_Account_Closed_Link_And_Foreign_Tenant_Client_Should_Be_Denied()
    {
        var staff = await SeedStaffAsync();
        var first = await InviteAndAcceptAsync(staff);
        var second = await InviteAndAcceptAsync(staff);

        var foreign = await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{first.LinkId}/clinic-disclosure", second.AccessToken);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(foreign)).Should().Be("Portal.Links.NotFound");
        (await SendAsync(HttpMethod.Post, $"/api/portal/v1/links/{first.LinkId}/clinic-disclosure/acknowledge", second.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Personel bağı başka müşteri kaydı üzerinden kapatamaz.
        (await SendAsync(HttpMethod.Post, $"/api/v1/clients/{second.ClientId}/portal-links/{first.LinkId}/close", staff.Token))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await SendAsync(HttpMethod.Post, $"/api/v1/clients/{first.ClientId}/portal-links/{first.LinkId}/close", staff.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{first.LinkId}/clinic-disclosure", first.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "kapatılan bağ anında erişimi keser");
        var me = await (await SendAsync(HttpMethod.Get, "/api/portal/v1/me", first.AccessToken)).Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("links").GetArrayLength().Should().Be(0, "hesap açık kalır, bağ listeden düşer");
        (await SendAsync(HttpMethod.Post, $"/api/v1/clients/{first.ClientId}/portal-links/{first.LinkId}/close", staff.Token))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Başka kiracının müşterisi personel uçlarında görünmez.
        Guid foreignClientId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant($"PortalYabanci-{Guid.NewGuid():N}"[..24]);
            db.Tenants.Add(tenant);
            var foreignClient = new Client(tenant.Id, "Yabancı Sahip", email: $"foreign-{Guid.NewGuid():N}@example.com");
            db.Clients.Add(foreignClient);
            await db.SaveChangesAsync();
            foreignClientId = foreignClient.Id;
        }

        (await SendAsync(HttpMethod.Get, $"/api/v1/clients/{foreignClientId}/portal-access", staff.Token))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, $"/api/v1/clients/{foreignClientId}/portal-invites", staff.Token))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_Without_OwnerPortal_Manage_Should_Be_Forbidden()
    {
        var staff = await SeedStaffAsync([PermissionCatalog.Clients.Read]);
        var client = await SeedClientAsync(staff.TenantId);

        (await SendAsync(HttpMethod.Post, $"/api/v1/clients/{client.Id}/portal-invites", staff.Token))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, $"/api/v1/clients/{client.Id}/portal-access", staff.Token))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Revoked_Invite_Should_Not_Be_Accepted_And_New_Invite_Supersedes_Old()
    {
        var staff = await SeedStaffAsync();
        var client = await SeedClientAsync(staff.TenantId);
        var firstToken = await InviteAsync(staff, client.Id);
        var secondToken = await InviteAsync(staff, client.Id);

        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/invites/{firstToken}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var access = await (await SendAsync(HttpMethod.Get, $"/api/v1/clients/{client.Id}/portal-access", staff.Token)).Content.ReadFromJsonAsync<JsonElement>();
        var pending = access.GetProperty("invites").EnumerateArray().Single(i => i.GetProperty("status").GetString() == "Pending");
        var inviteId = pending.GetProperty("inviteId").GetGuid();

        (await SendAsync(HttpMethod.Post, $"/api/v1/clients/{client.Id}/portal-invites/{inviteId}/revoke", staff.Token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Post, $"/api/portal/v1/invites/{secondToken}/accept", body: new { acceptedLegalDocuments = PortalDocuments() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, $"/api/v1/clients/{client.Id}/portal-invites/{inviteId}/revoke", staff.Token))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Gerçek üreteci sarar; ham token ve kodları test için kaydeder.</summary>
    private sealed class RecordingSecretGenerator : IOwnerPortalSecretGenerator
    {
        private readonly OwnerPortalSecretGenerator _inner = new();
        private readonly ConcurrentQueue<string> _tokens = new();
        private readonly ConcurrentQueue<string> _codes = new();

        public string? LastToken => _tokens.LastOrDefault();
        public string? LastCode => _codes.LastOrDefault();
        public int TokenCount => _tokens.Count;
        public int CodeCount => _codes.Count;

        public string CreateToken()
        {
            var token = _inner.CreateToken();
            _tokens.Enqueue(token);
            return token;
        }

        public string CreateNumericCode(int length)
        {
            var code = _inner.CreateNumericCode(length);
            _codes.Enqueue(code);
            return code;
        }
    }
}
