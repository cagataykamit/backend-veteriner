using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ExternalApi;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.ExternalApi;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.ExternalApi;

/// <summary>
/// Dış API (TIER-009; docs/EXTERNAL_API_CONTRACT.md): kiracı yalnız anahtardan çözülür ve başka kiracının verisi görünmez; yanlış ve
/// iptal edilmiş anahtar 401; paket düşünce 403; panel JWT'si dış uçta, anahtar panel ucunda reddedilir; hassas alanlar yanıtta yok;
/// imleçli sayfa ve <c>updatedSince</c>; anahtar başına hız sınırı. Anahtarlar her koşuda panelden üretilir, sabit anahtar yoktur.
/// </summary>
public sealed class ExternalApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Keys = "/api/v1/api-keys";
    private const string External = "/api/external/v1";

    // Yanıtta bulunmaması gereken, tohum veride dolu hassas değerler (sahte, test için).
    private const string NationalId = "10000000146";
    private const string Address = "Gizli Sokak No 1";
    private const string PetNote = "PetNotuGizli";
    private const string Microchip = "900000000000001";
    private const string AppointmentNote = "RandevuNotuGizli";
    private const string VaccinationNote = "AsiNotuGizli";

    private static readonly string[] AllScopes = [.. ApiKeyScopes.All];

    private readonly CustomWebApplicationFactory _factory;

    public ExternalApiIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Key_Should_Only_See_Its_Own_Tenant_And_Reject_Tenant_Parameter()
    {
        var a = await SeedTenantAsync(SubscriptionPlanCode.Premium);
        var b = await SeedTenantAsync(SubscriptionPlanCode.Premium);
        var keyA = await a.CreateKeyAsync("A entegrasyonu", AllScopes);
        var keyB = await b.CreateKeyAsync("B entegrasyonu", AllScopes);
        var httpA = ExternalClient(keyA.PlainTextKey);

        foreach (var (resource, idsOf) in Resources())
        {
            var page = await GetPageAsync(httpA, $"{External}/{resource}?pageSize=200");
            var ids = page.Select(e => e.GetProperty("id").GetGuid()).ToHashSet();
            ids.Should().Contain(idsOf(a), $"{resource}: kendi kiracısının kaydı görünür");
            ids.Should().NotContain(idsOf(b), $"{resource}: başka kiracının kaydı görünmez");
        }

        var forbiddenTenant = await httpA.GetAsync($"{External}/clients?tenantId={b.TenantId}");
        forbiddenTenant.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(forbiddenTenant)).Should().Be(ExternalApiErrors.ValidationCode);

        var pageB = await GetPageAsync(ExternalClient(keyB.PlainTextKey), $"{External}/clients?pageSize=200");
        pageB.Select(e => e.GetProperty("id").GetGuid()).Should().Contain(b.ClientId).And.NotContain(a.ClientId);

        // Panel listesi de kiracıya bağlı: A, B'nin anahtarını görmez ve iptal edemez.
        var listA = (await a.Http.GetFromJsonAsync<ApiKeyListDto>(Keys))!;
        listA.Items.Select(k => k.Id).Should().Contain(keyA.Key.Id).And.NotContain(keyB.Key.Id);
        (await a.Http.PostAsync($"{Keys}/{keyB.Key.Id}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Responses_Should_Not_Contain_Sensitive_Fields_Or_Key_Material()
    {
        var env = await SeedTenantAsync(SubscriptionPlanCode.Premium);
        var created = await env.CreateKeyAsync("Hassas alan denetimi", AllScopes);
        var http = ExternalClient(created.PlainTextKey);

        foreach (var (resource, _) in Resources())
        {
            var raw = await http.GetStringAsync($"{External}/{resource}?pageSize=200");
            foreach (var value in new[] { NationalId, Address, PetNote, Microchip, AppointmentNote, VaccinationNote })
                raw.Should().NotContain(value, $"{resource} yanıtı hassas değer taşımamalı");
            foreach (var field in new[] { "nationalId", "taxNumber", "address", "notes", "microchip", "kvkk", "alert", "veterinarian", "hbs" })
                raw.Should().NotContainEquivalentOf($"\"{field}", $"{resource} yanıtı '{field}' alanı taşımamalı");
        }

        var panelList = await env.Http.GetStringAsync(Keys);
        panelList.Should().NotContain(created.PlainTextKey).And.NotContainEquivalentOf("secretHash").And.NotContainEquivalentOf("plainTextKey");

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.ApiKeys.SingleAsync(k => k.Id == created.Key.Id);
        stored.SecretHash.Should().Equal(ApiKeySecret.Hash(created.PlainTextKey), "sunucuda yalnız özet saklanır");
        created.PlainTextKey.Should().StartWith(stored.Prefix);
        var audit = await db.AuditLogs.Where(l => l.Action == "ApiKey.Create" && l.TargetId!.Contains("Hassas alan denetimi")).ToListAsync();
        audit.Should().ContainSingle().Which.Success.Should().BeTrue();
        (audit[0].RequestPayload ?? string.Empty).Should().NotContain(created.PlainTextKey);
    }

    [Fact]
    public async Task Wrong_Missing_And_Revoked_Keys_Should_Return_401()
    {
        var env = await SeedTenantAsync(SubscriptionPlanCode.Premium);
        var created = await env.CreateKeyAsync("İptal denemesi", [ApiKeyScopes.ClientsRead]);
        var key = created.PlainTextKey;
        var tampered = key[..^1] + (key[^1] == 'A' ? 'B' : 'A');
        var unknown = ApiKeySecret.Generate().PlainText;

        await ShouldBe401Async(_factory.CreateClient(), ExternalApiErrors.KeyMissingCode);
        await ShouldBe401Async(ExternalClient("gecersiz"), ExternalApiErrors.KeyInvalidCode);
        await ShouldBe401Async(ExternalClient(tampered), ExternalApiErrors.KeyInvalidCode);
        await ShouldBe401Async(ExternalClient(unknown), ExternalApiErrors.KeyInvalidCode);
        (await ExternalClient(key).GetAsync($"{External}/clients")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await env.Http.PostAsync($"{Keys}/{created.Key.Id}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await env.Http.PostAsync($"{Keys}/{created.Key.Id}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await ShouldBe401Async(ExternalClient(key), ExternalApiErrors.KeyRevokedCode);

        var listed = (await env.Http.GetFromJsonAsync<ApiKeyListDto>(Keys))!.Items.Single(k => k.Id == created.Key.Id);
        listed.IsRevoked.Should().BeTrue();
        listed.RevokedByName.Should().NotBeNullOrWhiteSpace();
        listed.LastUsedAtUtc.Should().NotBeNull("başarılı istek son kullanımı yazar");

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogs.AnyAsync(l => l.Action == "ApiKey.Revoke" && l.Success && l.TargetId == $"ApiKeyId={created.Key.Id}"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Plan_Downgrade_Should_Keep_Keys_But_Return_403_And_Block_New_Keys()
    {
        var env = await SeedTenantAsync(SubscriptionPlanCode.Premium);
        var created = await env.CreateKeyAsync("Paket denemesi", [ApiKeyScopes.ClientsRead]);
        (await ExternalClient(created.PlainTextKey).GetAsync($"{External}/clients")).StatusCode.Should().Be(HttpStatusCode.OK);

        await SetPlanAsync(env.TenantId, SubscriptionPlanCode.Pro);

        var denied = await ExternalClient(created.PlainTextKey).GetAsync($"{External}/clients");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await denied.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("Subscriptions.FeatureNotInPlan");
        json.GetProperty("requiredPlanCode").GetString().Should().Be("Premium");

        var list = (await env.Http.GetFromJsonAsync<ApiKeyListDto>(Keys))!;
        list.Items.Should().ContainSingle(k => k.Id == created.Key.Id && !k.IsRevoked, "paket düşünce anahtar silinmez");
        var create = await env.Http.PostAsJsonAsync(Keys, new { name = "Yeni", scopes = new[] { ApiKeyScopes.ClientsRead } });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(create)).Should().Be("Subscriptions.FeatureNotInPlan");
        (await env.Http.PostAsync($"{Keys}/{created.Key.Id}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NoContent, "iptal her pakette açık");
    }

    [Fact]
    public async Task Panel_Jwt_And_Api_Key_Should_Not_Cross_Over()
    {
        var env = await SeedTenantAsync(SubscriptionPlanCode.Premium);
        var created = await env.CreateKeyAsync("Kimlik ayrımı", AllScopes);

        // Panel JWT'si dış uçta geçmez; anahtarla birlikte gönderilse de.
        var jwtOnly = _factory.CreateClient();
        jwtOnly.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", env.Token);
        await ShouldBe401Async(jwtOnly, ExternalApiErrors.BearerNotAcceptedCode);
        var both = ExternalClient(created.PlainTextKey);
        both.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", env.Token);
        await ShouldBe401Async(both, ExternalApiErrors.BearerNotAcceptedCode);

        // Anahtar panel ucunda geçmez: ne kendi başlığıyla ne de Bearer olarak.
        (await ExternalClient(created.PlainTextKey).GetAsync("/api/v1/clients")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ExternalClient(created.PlainTextKey).GetAsync(Keys)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var keyAsBearer = _factory.CreateClient();
        keyAsBearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.PlainTextKey);
        (await keyAsBearer.GetAsync("/api/v1/clients")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Anahtar yönetimi yalnız ApiKeys.Manage izniyle.
        var noPermission = await env.NewHttpAsync([PermissionCatalog.Clients.Read]);
        (await noPermission.GetAsync(Keys)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await noPermission.PostAsJsonAsync(Keys, new { name = "X", scopes = AllScopes })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Key_Should_Only_Reach_Endpoints_In_Its_Scopes()
    {
        var env = await SeedTenantAsync(SubscriptionPlanCode.Premium);
        var created = await env.CreateKeyAsync("Yalnız müşteri", [ApiKeyScopes.ClientsRead]);
        var http = ExternalClient(created.PlainTextKey);

        (await http.GetAsync($"{External}/clients")).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var resource in new[] { "pets", "appointments", "vaccinations" })
        {
            var response = await http.GetAsync($"{External}/{resource}");
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, resource);
            (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be(ExternalApiErrors.ScopeMissingCode);
        }

        var invalidScope = await env.Http.PostAsJsonAsync(Keys, new { name = "Yazma", scopes = new[] { "clients:write" } });
        invalidScope.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Paging_Should_Follow_Cursor_And_UpdatedSince()
    {
        var env = await SeedTenantAsync(SubscriptionPlanCode.Premium);
        var extra1 = await env.SeedClientAsync("Sayfa İki");
        var extra2 = await env.SeedClientAsync("Sayfa Üç");
        var http = ExternalClient((await env.CreateKeyAsync("Sayfa", [ApiKeyScopes.ClientsRead])).PlainTextKey);

        var all = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var response = await http.GetFromJsonAsync<JsonElement>($"{External}/clients?pageSize=2{(cursor is null ? "" : $"&cursor={cursor}")}");
            response.GetProperty("pageSize").GetInt32().Should().Be(2);
            var items = response.GetProperty("items").EnumerateArray().ToList();
            items.Count.Should().BeLessThanOrEqualTo(2);
            all.AddRange(items.Select(i => i.GetProperty("id").GetGuid()));
            cursor = response.GetProperty("nextCursor").ValueKind == JsonValueKind.Null ? null : response.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 10);

        all.Should().BeEquivalentTo([env.ClientId, extra1, extra2]).And.OnlyHaveUniqueItems();
        pages.Should().Be(2);

        (await http.GetAsync($"{External}/clients?cursor=bozuk")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await http.GetFromJsonAsync<JsonElement>($"{External}/clients?pageSize=100000")).GetProperty("pageSize").GetInt32()
            .Should().Be(ExternalPaging.MaxPageSize);

        // Değişen kayıt updatedSince ile yeniden gelir (kaydetme anında damgalanır).
        var since = DateTime.UtcNow;
        (await GetPageAsync(http, $"{External}/clients?updatedSince={since:O}")).Should().BeEmpty();
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = await db.Clients.SingleAsync(c => c.Id == extra1);
            client.UpdateDetails("Sayfa İki Güncel", null, client.Phone, null, DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        var changed = await GetPageAsync(http, $"{External}/clients?updatedSince={since:O}");
        changed.Select(e => e.GetProperty("id").GetGuid()).Should().Equal(extra1);
        changed[0].GetProperty("fullName").GetString().Should().Be("Sayfa İki Güncel");
    }

    [Fact]
    public async Task Rate_Limit_Should_Apply_Per_Key()
    {
        var env = await SeedTenantAsync(SubscriptionPlanCode.Premium);
        var first = await env.CreateKeyAsync("Sınır 1", [ApiKeyScopes.ClientsRead]);
        var second = await env.CreateKeyAsync("Sınır 2", [ApiKeyScopes.ClientsRead]);

        await using var limited = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "true",
                ["ExternalApi:PermitLimitPerMinute"] = "3",
            })));

        var http = limited.CreateClient();
        http.DefaultRequestHeaders.Add(ExternalApiAuth.HeaderName, first.PlainTextKey);
        for (var i = 0; i < 3; i++)
            (await http.GetAsync($"{External}/clients")).StatusCode.Should().Be(HttpStatusCode.OK, $"istek {i + 1}");

        var limitedResponse = await http.GetAsync($"{External}/clients");
        limitedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await IntegrationTestProblemDetails.ReadCodeAsync(limitedResponse)).Should().Be("RateLimit.Exceeded");

        var other = limited.CreateClient();
        other.DefaultRequestHeaders.Add(ExternalApiAuth.HeaderName, second.PlainTextKey);
        (await other.GetAsync($"{External}/clients")).StatusCode.Should().Be(HttpStatusCode.OK, "sınır anahtar başınadır");
    }

    // ---- Yardımcılar ----

    private static IEnumerable<(string Resource, Func<Env, Guid> IdOf)> Resources()
    {
        yield return ("clients", e => e.ClientId);
        yield return ("pets", e => e.PetId);
        yield return ("appointments", e => e.AppointmentId);
        yield return ("vaccinations", e => e.VaccinationId);
    }

    private HttpClient ExternalClient(string key)
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Add(ExternalApiAuth.HeaderName, key);
        return http;
    }

    private static async Task<List<JsonElement>> GetPageAsync(HttpClient http, string url)
    {
        var response = await http.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("items").EnumerateArray().ToList();
    }

    private static async Task ShouldBe401Async(HttpClient http, string code)
    {
        var response = await http.GetAsync($"{External}/clients");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be(code);
    }

    private async Task SetPlanAsync(Guid tenantId, SubscriptionPlanCode plan)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == tenantId);
        subscription.ActivatePaidPlan(plan, DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    private async Task<Env> SeedTenantAsync(SubscriptionPlanCode plan)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId, clinicId, clientId, petId, appointmentId, vaccinationId;
        string email;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant($"Ext-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
            db.Tenants.Add(tenant);
            var subscription = TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);
            subscription.ActivatePaidPlan(plan, DateTime.UtcNow);
            db.TenantSubscriptions.Add(subscription);
            var clinic = new Clinic(tenant.Id, $"Ext-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var client = new Client(tenant.Id, "Dış Müşteri", DateTime.UtcNow, Env.NextPhone(), "dis@example.com", Address);
            client.ApplyInvoicingIdentity(ClientPersonType.Individual, NationalId, null, null, null, null, null, null, DateTime.UtcNow);
            db.Clients.Add(client);
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(tenant.Id, client.Id, "Dış Pati", speciesId, notes: PetNote, microchipNumber: Microchip);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            var appointment = new Appointment(
                tenant.Id, clinic.Id, pet.Id, DateTime.UtcNow.AddDays(1), 30, AppointmentType.Checkup, AppointmentStatus.Scheduled, AppointmentNote);
            db.Appointments.Add(appointment);
            var vaccine = await db.VaccineDefinitions.OrderBy(v => v.Code).Select(v => new { v.Id, v.Name }).FirstAsync();
            var vaccination = new Vaccination(
                tenant.Id, pet.Id, clinic.Id, null, vaccine.Id, vaccine.Name, VaccinationStatus.Applied, DateTime.UtcNow.AddHours(-1), null,
                VaccinationNote, DateTime.UtcNow);
            db.Vaccinations.Add(vaccination);
            await db.SaveChangesAsync();

            email = await Env.SeedUserAsync(db, hasher, tenant.Id, clinic.Id);
            (tenantId, clinicId, clientId, petId, appointmentId, vaccinationId) =
                (tenant.Id, clinic.Id, client.Id, pet.Id, appointment.Id, vaccination.Id);
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services, email, [PermissionCatalog.ApiKeys.Manage, PermissionCatalog.Clients.Read]);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Env(_factory, hasher, http, token, tenantId, clinicId, clientId, petId, appointmentId, vaccinationId);
    }

    private sealed record Env(
        CustomWebApplicationFactory Factory,
        IPasswordHasher Hasher,
        HttpClient Http,
        string Token,
        Guid TenantId,
        Guid ClinicId,
        Guid ClientId,
        Guid PetId,
        Guid AppointmentId,
        Guid VaccinationId)
    {
        private static int _phoneSeed = 7_100_000;

        public static string NextPhone() => $"90555{Interlocked.Increment(ref _phoneSeed):D7}";

        public static async Task<string> SeedUserAsync(AppDbContext db, IPasswordHasher hasher, Guid tenantId, Guid clinicId)
        {
            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            var email = $"ext-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash(Guid.NewGuid().ToString("N")), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenantId, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinicId, DateTime.UtcNow));
            await db.SaveChangesAsync();
            return email;
        }

        public async Task<CreatedApiKeyDto> CreateKeyAsync(string name, string[] scopes)
        {
            var response = await Http.PostAsJsonAsync(Keys, new { name, scopes });
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
            return (await response.Content.ReadFromJsonAsync<CreatedApiKeyDto>())!;
        }

        public async Task<HttpClient> NewHttpAsync(string[] permissions)
        {
            string email;
            await using (var scope = Factory.Services.CreateSystemScope())
                email = await SeedUserAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), Hasher, TenantId, ClinicId);

            var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(Factory.Services, email, permissions);
            var http = Factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        public async Task<Guid> SeedClientAsync(string name)
        {
            await using var scope = Factory.Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client(TenantId, name, DateTime.UtcNow, NextPhone());
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            return client.Id;
        }
    }
}
