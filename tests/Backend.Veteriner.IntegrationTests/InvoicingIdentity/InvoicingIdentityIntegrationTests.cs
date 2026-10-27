using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.InvoicingIdentity;

/// <summary>
/// ADR-036 Dilim 1 uçtan uca (HTTP + LocalDB): müşteri fatura kimliği (maskeli okuma, tam değer yalnız Clients.Update,
/// veri kopyası, audit maskelemesi), klinik mükellef ayarları (varsayılan, izin, doğrulama), ücret KDV/satır türü ve
/// hasta HBS kimlik no. Bkz. docs/INVOICING_IDENTITY_API_CONTRACT.md.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class InvoicingIdentityIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Tckn = "10000000146";
    private const string MaskedTckn = "100******46";
    private const string Vkn = "1234567890";

    private static readonly string[] EditorPermissions =
    [
        PermissionCatalog.Clients.Read, PermissionCatalog.Clients.Create, PermissionCatalog.Clients.Update,
        PermissionCatalog.Pets.Read, PermissionCatalog.Pets.Create, PermissionCatalog.Pets.Update,
        PermissionCatalog.Clinics.Read, PermissionCatalog.Clinics.Update,
        PermissionCatalog.Charges.Read, PermissionCatalog.Charges.Create, PermissionCatalog.Charges.Correct,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public InvoicingIdentityIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Client_Identity_Should_Be_Masked_On_Read_Surfaces_And_Full_Only_On_Edit_Endpoint()
    {
        var ctx = await SeedAsync(EditorPermissions);

        var clientId = await ctx.CreateClientAsync(new
        {
            personType = "Individual", nationalId = Tckn, billingProvince = "İstanbul", billingDistrict = "Kadıköy",
            billingAddressLine = "Moda Cad. 1",
        });

        var detail = await ctx.Http.GetAsync($"/api/v1/clients/{clientId}");
        var detailBody = await detail.Content.ReadAsStringAsync();
        detail.StatusCode.Should().Be(HttpStatusCode.OK, detailBody);
        var invoicing = JsonDocument.Parse(detailBody).RootElement.GetProperty("invoicing");
        invoicing.GetProperty("personType").GetString().Should().Be("Individual");
        invoicing.GetProperty("nationalIdMasked").GetString().Should().Be(MaskedTckn);
        invoicing.GetProperty("billingDistrict").GetString().Should().Be("Kadıköy");
        detailBody.Should().NotContain(Tckn);

        var export = await ctx.Http.GetAsync($"/api/v1/clients/{clientId}/data-export");
        var exportBody = await export.Content.ReadAsStringAsync();
        export.StatusCode.Should().Be(HttpStatusCode.OK, exportBody);
        JsonDocument.Parse(exportBody).RootElement.GetProperty("client").GetProperty("invoicing")
            .GetProperty("nationalIdMasked").GetString().Should().Be(MaskedTckn);
        exportBody.Should().NotContain(Tckn);

        var full = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{clientId}/invoicing-identity");
        full.GetProperty("nationalId").GetString().Should().Be(Tckn);
        full.GetProperty("billingAddressLine").GetString().Should().Be("Moda Cad. 1");

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var createLog = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "Client.Create" && a.ActorUserId == ctx.UserId)
            .SingleAsync();
        createLog.RequestPayload.Should().Contain(MaskedTckn).And.NotContain(Tckn);
        (await db.AuditLogs.AsNoTracking()
            .CountAsync(a => a.Action == "Client.InvoicingIdentity.Read" && a.TargetId == $"ClientId={clientId}"))
            .Should().Be(1);
    }

    [Fact]
    public async Task Reader_Without_Clients_Update_Should_Not_Get_Full_Identity()
    {
        var ctx = await SeedAsync(EditorPermissions);
        var clientId = await ctx.CreateClientAsync(new { personType = "Corporate", taxNumber = Vkn, taxOffice = "Kadıköy VD", legalName = "Örnek Ltd." });
        var reader = await ctx.ClientWithPermissionsAsync([PermissionCatalog.Clients.Read]);

        (await reader.GetAsync($"/api/v1/clients/{clientId}/invoicing-identity")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var detailBody = await reader.GetStringAsync($"/api/v1/clients/{clientId}");
        JsonDocument.Parse(detailBody).RootElement.GetProperty("invoicing")
            .GetProperty("taxNumberMasked").GetString().Should().Be("123*****90");
        detailBody.Should().NotContain(Vkn);
    }

    [Fact]
    public async Task Corporate_Client_Should_Accept_Tckn_As_Tax_Number_And_Mask_It()
    {
        var ctx = await SeedAsync(EditorPermissions);

        var clientId = await ctx.CreateClientAsync(new { personType = "Corporate", taxNumber = Tckn, taxOffice = "Kadıköy VD", legalName = "Ayşe Yılmaz" });

        var detailBody = await ctx.Http.GetStringAsync($"/api/v1/clients/{clientId}");
        JsonDocument.Parse(detailBody).RootElement.GetProperty("invoicing")
            .GetProperty("taxNumberMasked").GetString().Should().Be(MaskedTckn);
        detailBody.Should().NotContain(Tckn);
        var full = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{clientId}/invoicing-identity");
        full.GetProperty("taxNumber").GetString().Should().Be(Tckn);
        full.GetProperty("nationalId").ValueKind.Should().Be(JsonValueKind.Null);

        var invalid = await ctx.Http.PostAsJsonAsync("/api/v1/clients", new
        {
            fullName = "Geçersiz Vergi No",
            invoicingIdentity = new { personType = "Corporate", taxNumber = "12345678901" },
        });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync()).Should().Contain("10 haneli geçerli VKN veya 11 haneli geçerli TCKN");
    }

    [Fact]
    public async Task Invalid_Or_Inconsistent_Identity_Should_Return_400()
    {
        var ctx = await SeedAsync(EditorPermissions);

        var invalidTckn = await ctx.Http.PostAsJsonAsync("/api/v1/clients", new
        {
            fullName = "Geçersiz Kimlik",
            invoicingIdentity = new { personType = "Individual", nationalId = "12345678901" },
        });
        invalidTckn.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidTckn.Content.ReadAsStringAsync()).Should().Contain("TCKN 11 haneli ve geçerli olmalıdır.");

        var inconsistent = await ctx.Http.PostAsJsonAsync("/api/v1/clients", new
        {
            fullName = "Tutarsız Kimlik",
            invoicingIdentity = new { personType = "Individual", taxNumber = Vkn },
        });
        inconsistent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(inconsistent)).Should().Be("Clients.Validation");
    }

    [Fact]
    public async Task Client_Update_Without_Identity_Should_Keep_And_With_Identity_Should_Replace()
    {
        var ctx = await SeedAsync(EditorPermissions);
        var clientId = await ctx.CreateClientAsync(new { personType = "Individual", nationalId = Tckn, billingProvince = "İzmir" });

        (await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{clientId}", new { fullName = "Adı Değişti" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var kept = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{clientId}/invoicing-identity");
        kept.GetProperty("nationalId").GetString().Should().Be(Tckn);
        kept.GetProperty("billingProvince").GetString().Should().Be("İzmir");

        var replaced = await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{clientId}", new
        {
            fullName = "Adı Değişti",
            invoicingIdentity = new { personType = "Corporate", taxNumber = Vkn, legalName = "Örnek Ltd." },
        });
        replaced.StatusCode.Should().Be(HttpStatusCode.NoContent, await replaced.Content.ReadAsStringAsync());
        var after = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{clientId}/invoicing-identity");
        after.GetProperty("personType").GetString().Should().Be("Corporate");
        after.GetProperty("nationalId").ValueKind.Should().Be(JsonValueKind.Null);
        after.GetProperty("billingProvince").ValueKind.Should().Be(JsonValueKind.Null);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payloads = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "Client.Update" && a.TargetId == $"ClientId={clientId}")
            .Select(a => a.RequestPayload)
            .ToListAsync();
        payloads.Should().HaveCount(2);
        payloads.Should().OnlyContain(p => p != null && !p.Contains(Vkn));
    }

    [Fact]
    public async Task Taxpayer_Settings_Should_Default_Update_With_Permission_And_Validate()
    {
        var ctx = await SeedAsync(EditorPermissions);
        var url = $"/api/v1/clinics/{ctx.ClinicId}/taxpayer-settings";

        var defaults = await ctx.Http.GetFromJsonAsync<JsonElement>(url);
        defaults.GetProperty("isConfigured").GetBoolean().Should().BeFalse();
        defaults.GetProperty("defaultVatRate").GetDecimal().Should().Be(20m);
        defaults.GetProperty("withholdingRate").ValueKind.Should().Be(JsonValueKind.Null);

        var body = new
        {
            taxpayerType = "SelfEmployed", taxIdentityNumber = Tckn, taxOffice = "Kadıköy VD", taxpayerName = "Dr. Ayşe Yılmaz",
            province = "İstanbul", district = "Kadıköy", addressLine = "Moda Cad. 1", defaultVatRate = 20m, withholdingRate = 20m,
        };
        var saved = await ctx.Http.PutAsJsonAsync(url, body);
        var savedBody = await saved.Content.ReadAsStringAsync();
        saved.StatusCode.Should().Be(HttpStatusCode.OK, savedBody);
        var savedJson = JsonDocument.Parse(savedBody).RootElement;
        savedJson.GetProperty("isConfigured").GetBoolean().Should().BeTrue();
        savedJson.GetProperty("taxpayerType").GetString().Should().Be("SelfEmployed");
        savedJson.GetProperty("withholdingRate").GetDecimal().Should().Be(20m);

        var invalid = await ctx.Http.PutAsJsonAsync(url, body with { taxIdentityNumber = "1234567891" });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var reader = await ctx.ClientWithPermissionsAsync([PermissionCatalog.Clinics.Read]);
        (await reader.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reader.PutAsJsonAsync(url, body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "Clinic.TaxpayerSettings.Update" && a.TargetId == $"ClinicId={ctx.ClinicId}" && a.Success)
            .SingleAsync();
        log.RequestPayload.Should().Contain(MaskedTckn).And.NotContain(Tckn);
    }

    [Fact]
    public async Task Charge_Should_Store_Line_Kind_And_Vat_And_Correct_Tax_Only()
    {
        var ctx = await SeedAsync(EditorPermissions);
        var clientId = await ctx.CreateClientAsync(null);

        var defaultCharge = await ctx.PostChargeAsync(clientId, null);
        defaultCharge.GetProperty("lineKind").GetString().Should().Be("Service");
        defaultCharge.GetProperty("vatRate").ValueKind.Should().Be(JsonValueKind.Null);

        var productCharge = await ctx.PostChargeAsync(clientId, new { lineKind = "Product", vatRate = 10m });
        productCharge.GetProperty("lineKind").GetString().Should().Be("Product");
        productCharge.GetProperty("vatRate").GetDecimal().Should().Be(10m);

        var invalid = await ctx.Http.PostAsJsonAsync("/api/v1/charges", ChargeBody(ctx.ClinicId, clientId, new { lineKind = "Service", vatRate = 150m }));
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(invalid)).Should().Be("Charges.Validation");

        var chargeId = defaultCharge.GetProperty("id").GetGuid();
        var corrected = await ctx.Http.PutAsJsonAsync($"/api/v1/charges/{chargeId}", new
        {
            description = "Genel muayene", quantity = 1m, unitPrice = 750m, reason = "KDV oranı eklendi",
            tax = new { lineKind = "Service", vatRate = 20m },
        });
        var correctedBody = await corrected.Content.ReadAsStringAsync();
        corrected.StatusCode.Should().Be(HttpStatusCode.OK, correctedBody);
        JsonDocument.Parse(correctedBody).RootElement.GetProperty("vatRate").GetDecimal().Should().Be(20m);
    }

    [Fact]
    public async Task Pet_Hbs_Animal_Id_Should_Be_Written_Kept_And_Cleared()
    {
        var ctx = await SeedAsync(EditorPermissions);
        var clientId = await ctx.CreateClientAsync(null);
        var speciesId = await ctx.FirstSpeciesIdAsync();
        var petName = $"Pamuk-{Guid.NewGuid():N}"[..12];

        var created = await ctx.Http.PostAsJsonAsync("/api/v1/pets", new { clientId, name = petName, speciesId, hbsAnimalId = " TR-0001 " });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var petId = await created.Content.ReadFromJsonAsync<Guid>();

        (await ctx.GetPetAsync(petId)).GetProperty("hbsAnimalId").GetString().Should().Be("TR-0001");

        (await ctx.Http.PutAsJsonAsync($"/api/v1/pets/{petId}", new { clientId, name = petName, speciesId }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ctx.GetPetAsync(petId)).GetProperty("hbsAnimalId").GetString().Should().Be("TR-0001");

        (await ctx.Http.PutAsJsonAsync($"/api/v1/pets/{petId}", new { clientId, name = petName, speciesId, hbsAnimalId = "" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ctx.GetPetAsync(petId)).GetProperty("hbsAnimalId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---- Kurulum ----

    private static object ChargeBody(Guid clinicId, Guid clientId, object? tax)
        => new { clinicId, clientId, description = "Genel muayene", quantity = 1m, unitPrice = 750m, tax };

    private async Task<TestContext> SeedAsync(string[] permissions)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid clinicId;
        Guid userId;
        string email;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Inv-{Guid.NewGuid():N}"[..14], "Istanbul");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"inv-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id, DateTime.UtcNow));
            await db.SaveChangesAsync();

            clinicId = clinic.Id;
            userId = user.Id;
        }

        var ctx = new TestContext(_factory, email, clinicId, userId);
        ctx.Http = await ctx.ClientWithPermissionsAsync(permissions);
        return ctx;
    }

    private sealed class TestContext(CustomWebApplicationFactory factory, string email, Guid clinicId, Guid userId)
    {
        public HttpClient Http { get; set; } = default!;
        public Guid ClinicId { get; } = clinicId;
        public Guid UserId { get; } = userId;

        public async Task<HttpClient> ClientWithPermissionsAsync(string[] permissions)
        {
            var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(factory.Services, email, permissions);
            var http = factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        public async Task<Guid> CreateClientAsync(object? invoicingIdentity)
        {
            var response = await Http.PostAsJsonAsync("/api/v1/clients", new
            {
                fullName = $"Fatura Müşteri {Guid.NewGuid():N}"[..24],
                invoicingIdentity,
            });
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.Created, body);
            return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
        }

        public async Task<JsonElement> PostChargeAsync(Guid clientId, object? tax)
        {
            var response = await Http.PostAsJsonAsync("/api/v1/charges", ChargeBody(ClinicId, clientId, tax));
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.Created, body);
            return JsonDocument.Parse(body).RootElement.GetProperty("charge");
        }

        public async Task<Guid> FirstSpeciesIdAsync()
        {
            await using var scope = factory.Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Species.Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        }

        public Task<JsonElement> GetPetAsync(Guid petId) => Http.GetFromJsonAsync<JsonElement>($"/api/v1/pets/{petId}");
    }
}
