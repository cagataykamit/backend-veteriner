using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Clients;

/// <summary>
/// ADR-021: müşteri KVKK rıza durumu (Kabul/Ret/Yok, tarih, işaretleyen, RowVersion/409, hatırlatma tercihine dokunmama)
/// ve klinik KVKK şablonları: açık rıza formu ve aydınlatma metni (ADR-024) (varsayılan, oluştur/güncelle, 409, izinler,
/// türlerin birbirinden bağımsızlığı); ADR-032: rıza kaydına aktif kliniğin açık rıza metni sürümü ve özeti.
/// HTTP + gerçek SQL Server (LocalDB).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class KvkkConsentIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AllPermissions =
    [
        PermissionCatalog.Clients.Read,
        PermissionCatalog.Clients.Create,
        PermissionCatalog.Clients.Update,
        PermissionCatalog.Clinics.Read,
        PermissionCatalog.Clinics.Update,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public KvkkConsentIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Consent_Should_Default_None_Record_Conflict_And_Revert()
    {
        var ctx = await SeedAsync();
        var before = await ctx.GetClientAsync();
        before.GetProperty("kvkkConsentStatus").GetString().Should().Be("None");
        before.GetProperty("kvkkConsentRecordedOn").ValueKind.Should().Be(JsonValueKind.Null);
        var rowVersion = before.GetProperty("rowVersion").GetString();
        rowVersion.Should().NotBeNullOrWhiteSpace();

        var accept = await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{ctx.ClientId}/kvkk-consent",
            new { status = "Accepted", recordedOn = "2026-01-02", rowVersion });
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        var accepted = await accept.Content.ReadFromJsonAsync<JsonElement>();
        accepted.GetProperty("kvkkConsentStatus").GetString().Should().Be("Accepted");
        accepted.GetProperty("kvkkConsentRecordedOn").GetString().Should().Be("2026-01-02");
        accepted.GetProperty("kvkkConsentRecordedByUserId").GetGuid().Should().Be(ctx.UserId);
        accepted.GetProperty("rowVersion").GetString().Should().NotBe(rowVersion);
        // Klinik bağlamı olmayan istek (token'da clinic_id yok): metin sürümü bilinmiyor (ADR-032).
        accepted.GetProperty("kvkkConsentTemplateVersion").ValueKind.Should().Be(JsonValueKind.Null);

        // Eski sürümle ikinci yazım 409.
        var stale = await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{ctx.ClientId}/kvkk-consent",
            new { status = "Declined", rowVersion });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(stale)).Should().Be("Clients.ConcurrencyConflict");

        // Detay yanıtı aynı alanları taşır; Kabul hatırlatma tercihini değiştirmez.
        var detail = await ctx.GetClientAsync();
        detail.GetProperty("kvkkConsentStatus").GetString().Should().Be("Accepted");
        detail.GetProperty("kvkkConsentRecordedOn").GetString().Should().Be("2026-01-02");
        detail.GetProperty("kvkkConsentRecordedByUserId").GetGuid().Should().Be(ctx.UserId);
        detail.GetProperty("remindersOptedOut").GetBoolean().Should().BeFalse();
        accepted.GetProperty("remindersOptedOut").GetBoolean().Should().BeFalse();

        // Ret, tarih verilmezse bugün; hatırlatma istemiyor bayrağını açar (ADR-025 madde 4).
        var decline = await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{ctx.ClientId}/kvkk-consent",
            new { status = "Declined", rowVersion = accepted.GetProperty("rowVersion").GetString() });
        decline.StatusCode.Should().Be(HttpStatusCode.OK, await decline.Content.ReadAsStringAsync());
        var declined = await decline.Content.ReadFromJsonAsync<JsonElement>();
        declined.GetProperty("kvkkConsentRecordedOn").GetString().Should().NotBeNullOrWhiteSpace();
        declined.GetProperty("remindersOptedOut").GetBoolean().Should().BeTrue();
        (await ctx.GetClientAsync()).GetProperty("remindersOptedOut").GetBoolean().Should().BeTrue();

        // "Yok"a geri alma: tarih ve işaretleyen temizlenir; bayrak kendiliğinden kapanmaz.
        var revert = await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{ctx.ClientId}/kvkk-consent",
            new { status = "None", rowVersion = declined.GetProperty("rowVersion").GetString() });
        revert.StatusCode.Should().Be(HttpStatusCode.OK, await revert.Content.ReadAsStringAsync());
        var reverted = await ctx.GetClientAsync();
        reverted.GetProperty("kvkkConsentStatus").GetString().Should().Be("None");
        reverted.GetProperty("kvkkConsentRecordedOn").ValueKind.Should().Be(JsonValueKind.Null);
        reverted.GetProperty("kvkkConsentRecordedByUserId").ValueKind.Should().Be(JsonValueKind.Null);
        reverted.GetProperty("remindersOptedOut").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Consent_Should_Validate_Body_Require_Clients_Update_And_Survive_Client_Put()
    {
        var ctx = await SeedAsync();
        var rv = (await ctx.GetClientAsync()).GetProperty("rowVersion").GetString();

        var future = await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{ctx.ClientId}/kvkk-consent",
            new { status = "Accepted", recordedOn = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"), rowVersion = rv });
        future.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var noneWithDate = await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{ctx.ClientId}/kvkk-consent",
            new { status = "None", recordedOn = "2026-01-02", rowVersion = rv });
        noneWithDate.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var noRowVersion = await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{ctx.ClientId}/kvkk-consent", new { status = "Accepted" });
        noRowVersion.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var invalidStatus = await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{ctx.ClientId}/kvkk-consent",
            new { status = "Maybe", rowVersion = rv });
        invalidStatus.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var readOnly = await SeedAsync([PermissionCatalog.Clients.Read]);
        var forbidden = await readOnly.Http.PutAsJsonAsync($"/api/v1/clients/{ctx.ClientId}/kvkk-consent",
            new { status = "Accepted", rowVersion = rv });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Rıza işaretlendikten sonra tam müşteri PUT'u rızayı silmez.
        (await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{ctx.ClientId}/kvkk-consent", new { status = "Accepted", rowVersion = rv }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var fullName = (await ctx.GetClientAsync()).GetProperty("fullName").GetString();
        (await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{ctx.ClientId}", new { fullName, phone = "05551110083", address = "Izmir" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ctx.GetClientAsync()).GetProperty("kvkkConsentStatus").GetString().Should().Be("Accepted");
    }

    [Fact]
    public async Task FormTemplate_Should_Return_Default_Create_Update_And_Conflict()
    {
        var ctx = await SeedAsync();
        var url = $"/api/v1/clinics/{ctx.ClinicId}/kvkk-form-template";

        var initial = await ctx.Http.GetFromJsonAsync<JsonElement>(url);
        initial.GetProperty("isDefault").GetBoolean().Should().BeTrue();
        initial.GetProperty("rowVersion").ValueKind.Should().Be(JsonValueKind.Null);
        initial.GetProperty("text").GetString().Should().Contain("hukuk danışmanınızca onaylanmış");
        initial.GetProperty("placeholders").EnumerateArray().Select(p => p.GetString())
            .Should().Equal("{{clinicName}}", "{{clientName}}", "{{date}}");

        var create = await ctx.Http.PutAsJsonAsync(url, new { text = "Metin {{clientName}} {{date}}" });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("isDefault").GetBoolean().Should().BeFalse();
        var createdRv = created.GetProperty("rowVersion").GetString();

        // Varsayılanı görüp sürümsüz kaydeden ikinci istemci 409.
        var raced = await ctx.Http.PutAsJsonAsync(url, new { text = "Başka metin" });
        raced.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(raced)).Should().Be("Clinics.KvkkFormTemplate.ConcurrencyConflict");

        var update = await ctx.Http.PutAsJsonAsync(url, new { text = "Güncel metin", rowVersion = createdRv });
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());

        var stale = await ctx.Http.PutAsJsonAsync(url, new { text = "Eski sürüm", rowVersion = createdRv });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var empty = await ctx.Http.PutAsJsonAsync(url, new { text = "   ", rowVersion = createdRv });
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var current = await ctx.Http.GetFromJsonAsync<JsonElement>(url);
        current.GetProperty("text").GetString().Should().Be("Güncel metin");
        current.GetProperty("isDefault").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task FormTemplate_Read_Should_Allow_Clients_Read_And_Put_Should_Require_Clinics_Update()
    {
        var admin = await SeedAsync();
        var printer = await SeedAsync([PermissionCatalog.Clients.Read], admin.ClinicId);
        var url = $"/api/v1/clinics/{admin.ClinicId}/kvkk-form-template";

        (await printer.Http.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await printer.Http.PutAsJsonAsync(url, new { text = "Metin" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var outsider = await SeedAsync([PermissionCatalog.Pets.Read], admin.ClinicId);
        (await outsider.Http.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DisclosureTemplate_Should_Return_Default_Create_Update_Conflict_And_Stay_Separate_From_Consent()
    {
        var ctx = await SeedAsync();
        var url = $"/api/v1/clinics/{ctx.ClinicId}/kvkk-disclosure-template";
        var consentUrl = $"/api/v1/clinics/{ctx.ClinicId}/kvkk-form-template";

        var initial = await ctx.Http.GetFromJsonAsync<JsonElement>(url);
        initial.GetProperty("isDefault").GetBoolean().Should().BeTrue();
        initial.GetProperty("rowVersion").ValueKind.Should().Be(JsonValueKind.Null);
        initial.GetProperty("text").GetString().Should().Contain("AYDINLATMA METNİ").And.Contain("hukuk danışmanınızca onaylanmış");
        initial.GetProperty("placeholders").EnumerateArray().Select(p => p.GetString())
            .Should().Equal("{{clinicName}}", "{{clientName}}", "{{date}}");

        // Açık rıza metni kaydedilince aydınlatma varsayılanda kalır (ayrı kayıt).
        (await ctx.Http.PutAsJsonAsync(consentUrl, new { text = "Rıza metni" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ctx.Http.GetFromJsonAsync<JsonElement>(url)).GetProperty("isDefault").GetBoolean().Should().BeTrue();

        var create = await ctx.Http.PutAsJsonAsync(url, new { text = "Aydınlatma {{clinicName}} {{date}}" });
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("isDefault").GetBoolean().Should().BeFalse();
        var createdRv = created.GetProperty("rowVersion").GetString();

        var raced = await ctx.Http.PutAsJsonAsync(url, new { text = "Başka metin" });
        raced.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(raced)).Should().Be("Clinics.KvkkFormTemplate.ConcurrencyConflict");

        var update = await ctx.Http.PutAsJsonAsync(url, new { text = "Güncel aydınlatma", rowVersion = createdRv });
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());

        var stale = await ctx.Http.PutAsJsonAsync(url, new { text = "Eski sürüm", rowVersion = createdRv });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var empty = await ctx.Http.PutAsJsonAsync(url, new { text = "   ", rowVersion = createdRv });
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await ctx.Http.GetFromJsonAsync<JsonElement>(url)).GetProperty("text").GetString().Should().Be("Güncel aydınlatma");
        (await ctx.Http.GetFromJsonAsync<JsonElement>(consentUrl)).GetProperty("text").GetString().Should().Be("Rıza metni");
    }

    [Fact]
    public async Task DisclosureTemplate_Read_Should_Allow_Clients_Read_And_Put_Should_Require_Clinics_Update()
    {
        var admin = await SeedAsync();
        var printer = await SeedAsync([PermissionCatalog.Clients.Read], admin.ClinicId);
        var url = $"/api/v1/clinics/{admin.ClinicId}/kvkk-disclosure-template";

        (await printer.Http.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await printer.Http.PutAsJsonAsync(url, new { text = "Metin" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var outsider = await SeedAsync([PermissionCatalog.Pets.Read], admin.ClinicId);
        (await outsider.Http.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Consent_Should_Record_Template_Version_And_Hash_Of_Active_Clinic()
    {
        var ctx = await SeedAsync(clinicScopedToken: true);
        var templateUrl = $"/api/v1/clinics/{ctx.ClinicId}/kvkk-form-template";
        var consentUrl = $"/api/v1/clients/{ctx.ClientId}/kvkk-consent";

        // Kayıt yokken taslak varsayılan metin: sürüm 0.
        var defaultTemplate = await ctx.Http.GetFromJsonAsync<JsonElement>(templateUrl);
        defaultTemplate.GetProperty("textVersion").GetInt32().Should().Be(0);
        var defaultHash = defaultTemplate.GetProperty("textHash").GetString();
        defaultHash.Should().Be(ClinicKvkkFormTemplate.ComputeTextHash(defaultTemplate.GetProperty("text").GetString()!));

        var accept = await ctx.Http.PutAsJsonAsync(consentUrl,
            new { status = "Accepted", rowVersion = (await ctx.GetClientAsync()).GetProperty("rowVersion").GetString() });
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        var accepted = await accept.Content.ReadFromJsonAsync<JsonElement>();
        accepted.GetProperty("kvkkConsentTemplateVersion").GetInt32().Should().Be(0);
        accepted.GetProperty("kvkkConsentTemplateHash").GetString().Should().Be(defaultHash);

        // İlk kayıt 1; aynı metin sürümü artırmaz; değişen metin artırır.
        var v1 = await (await ctx.Http.PutAsJsonAsync(templateUrl, new { text = "Metin A" })).Content.ReadFromJsonAsync<JsonElement>();
        v1.GetProperty("textVersion").GetInt32().Should().Be(1);
        var same = await ctx.Http.PutAsJsonAsync(templateUrl, new { text = " Metin A ", rowVersion = v1.GetProperty("rowVersion").GetString() });
        same.StatusCode.Should().Be(HttpStatusCode.OK, await same.Content.ReadAsStringAsync());
        var sameBody = await same.Content.ReadFromJsonAsync<JsonElement>();
        sameBody.GetProperty("textVersion").GetInt32().Should().Be(1);
        var v2 = await (await ctx.Http.PutAsJsonAsync(templateUrl, new { text = "Metin B", rowVersion = sameBody.GetProperty("rowVersion").GetString() }))
            .Content.ReadFromJsonAsync<JsonElement>();
        v2.GetProperty("textVersion").GetInt32().Should().Be(2);
        v2.GetProperty("textHash").GetString().Should().Be(ClinicKvkkFormTemplate.ComputeTextHash("Metin B"));

        // Önceki rıza kaydı değişmez; yeni işaretleme güncel sürümü yazar.
        var detail = await ctx.GetClientAsync();
        detail.GetProperty("kvkkConsentTemplateVersion").GetInt32().Should().Be(0);
        var decline = await ctx.Http.PutAsJsonAsync(consentUrl,
            new { status = "Declined", rowVersion = detail.GetProperty("rowVersion").GetString() });
        decline.StatusCode.Should().Be(HttpStatusCode.OK, await decline.Content.ReadAsStringAsync());
        var afterDecline = await ctx.GetClientAsync();
        afterDecline.GetProperty("kvkkConsentTemplateVersion").GetInt32().Should().Be(2);
        afterDecline.GetProperty("kvkkConsentTemplateHash").GetString().Should().Be(ClinicKvkkFormTemplate.ComputeTextHash("Metin B"));

        // "Yok"a dönüş sürümü de temizler.
        var revert = await ctx.Http.PutAsJsonAsync(consentUrl,
            new { status = "None", rowVersion = afterDecline.GetProperty("rowVersion").GetString() });
        revert.StatusCode.Should().Be(HttpStatusCode.OK, await revert.Content.ReadAsStringAsync());
        var reverted = await ctx.GetClientAsync();
        reverted.GetProperty("kvkkConsentTemplateVersion").ValueKind.Should().Be(JsonValueKind.Null);
        reverted.GetProperty("kvkkConsentTemplateHash").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Consent_Recorded_Before_Versioning_Should_Return_Unknown_Version()
    {
        var ctx = await SeedAsync(clinicScopedToken: true);
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            // ADR-032 öncesi kayıt: Kabul işaretli, sürüm alanları null (geriye dönük doldurulmaz).
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = await db.Clients.SingleAsync(c => c.Id == ctx.ClientId);
            client.RecordKvkkConsent(KvkkConsentStatus.Accepted, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 2), ctx.UserId, null, null, DateTime.UtcNow)
                .IsSuccess.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        var detail = await ctx.GetClientAsync();
        detail.GetProperty("kvkkConsentStatus").GetString().Should().Be("Accepted");
        detail.GetProperty("kvkkConsentTemplateVersion").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("kvkkConsentTemplateHash").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---- Kurulum ----

    private async Task<KvkkTestContext> SeedAsync(
        string[]? permissions = null,
        Guid? existingClinicId = null,
        bool clinicScopedToken = false)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid clinicId;
        Guid clientId;
        Guid userId;
        string email;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            if (existingClinicId is { } existing)
            {
                clinicId = existing;
            }
            else
            {
                var clinic = new Clinic(tenant.Id, $"Kvkk-{Guid.NewGuid():N}"[..14], "Izmir");
                db.Clinics.Add(clinic);
                clinicId = clinic.Id;
            }

            var client = new Client(tenant.Id, $"KClient-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110083");
            db.Clients.Add(client);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"kvkk-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinicId, DateTime.UtcNow));
            await db.SaveChangesAsync();

            clientId = client.Id;
            userId = user.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services, email, permissions ?? AllPermissions, clinicScopedToken ? clinicId : null);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new KvkkTestContext(http, clinicId, clientId, userId);
    }

    private sealed class KvkkTestContext(HttpClient http, Guid clinicId, Guid clientId, Guid userId)
    {
        public HttpClient Http { get; } = http;
        public Guid ClinicId { get; } = clinicId;
        public Guid ClientId { get; } = clientId;
        public Guid UserId { get; } = userId;

        public async Task<JsonElement> GetClientAsync()
            => await Http.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{ClientId}");
    }
}
