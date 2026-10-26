using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Pets;

/// <summary>
/// ADR-020 S5: hayvan durumu (vefat/geri alma, RowVersion/409, liste süzgeci, yeni bakım kapısı) ve müşteri iletişim
/// tercihi (oluştur/güncelle/oku, PUT'ta alan yoksa dokunulmaz). HTTP + gerçek SQL Server (LocalDB).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class PetStatusAndClientContactIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AllPermissions =
    [
        PermissionCatalog.Pets.Read,
        PermissionCatalog.Pets.Create,
        PermissionCatalog.Pets.Update,
        PermissionCatalog.Clients.Read,
        PermissionCatalog.Clients.Create,
        PermissionCatalog.Clients.Update,
        PermissionCatalog.Visits.Read,
        PermissionCatalog.Visits.Create,
        PermissionCatalog.Appointments.Create,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public PetStatusAndClientContactIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task New_Pet_Should_Be_Active_With_RowVersion()
    {
        var ctx = await SeedAsync();
        var petId = await ctx.CreatePetAsync("StatusPet");

        var detail = await ctx.GetDetailAsync(petId);

        detail.Status.Should().Be(PetStatus.Active);
        detail.DeceasedOn.Should().BeNull();
        detail.RowVersion.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Mark_Deceased_Should_Block_New_Care_Filter_List_And_Revert()
    {
        var ctx = await SeedAsync();
        var deceasedId = await ctx.CreatePetAsync("VefatPet");
        var activeId = await ctx.CreatePetAsync("AktifPet");
        var before = await ctx.GetDetailAsync(deceasedId);

        var mark = await ctx.Http.PutAsJsonAsync($"/api/v1/pets/{deceasedId}/status",
            new { status = "Deceased", deceasedOn = "2026-01-02", rowVersion = before.RowVersion });
        mark.StatusCode.Should().Be(HttpStatusCode.OK, await mark.Content.ReadAsStringAsync());
        var marked = await mark.Content.ReadFromJsonAsync<JsonElement>();
        marked.GetProperty("status").GetString().Should().Be("Deceased");
        marked.GetProperty("deceasedOn").GetString().Should().Be("2026-01-02");

        // Eski sürümle ikinci yazım 409.
        var stale = await ctx.Http.PutAsJsonAsync($"/api/v1/pets/{deceasedId}/status",
            new { status = "Active", rowVersion = before.RowVersion });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(stale)).Should().Be("Pets.ConcurrencyConflict");

        // Yeni geliş ve randevu açılamaz.
        var visit = await ctx.Http.PostAsJsonAsync("/api/v1/visits", new { clinicId = ctx.ClinicId, petId = deceasedId });
        visit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(visit)).Should().Be("Pets.Deceased");

        // Liste: süzgeçsiz tümü, süzgeçle ayrık.
        (await ctx.ListPetIdsAsync(null)).Should().Contain(new[] { deceasedId, activeId });
        (await ctx.ListPetIdsAsync("Deceased")).Should().Equal(deceasedId);
        (await ctx.ListPetIdsAsync("Active")).Should().Equal(activeId);
        var listed = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/pets?clientId={ctx.ClientId}&pageSize=50");
        listed.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == deceasedId)
            .GetProperty("status").GetString().Should().Be("Deceased");

        // Geri alma: Aktif, tarih temizlenir, geliş açılabilir.
        var revert = await ctx.Http.PutAsJsonAsync($"/api/v1/pets/{deceasedId}/status",
            new { status = "Active", rowVersion = marked.GetProperty("rowVersion").GetString() });
        revert.StatusCode.Should().Be(HttpStatusCode.OK, await revert.Content.ReadAsStringAsync());
        var reverted = await ctx.GetDetailAsync(deceasedId);
        reverted.Status.Should().Be(PetStatus.Active);
        reverted.DeceasedOn.Should().BeNull();
        (await ctx.Http.PostAsJsonAsync("/api/v1/visits", new { clinicId = ctx.ClinicId, petId = deceasedId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Status_Should_Validate_Body_And_Require_Pets_Update()
    {
        var ctx = await SeedAsync();
        var petId = await ctx.CreatePetAsync("ValPet");
        var rv = (await ctx.GetDetailAsync(petId)).RowVersion;

        var future = await ctx.Http.PutAsJsonAsync($"/api/v1/pets/{petId}/status",
            new { status = "Deceased", deceasedOn = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"), rowVersion = rv });
        future.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var noRowVersion = await ctx.Http.PutAsJsonAsync($"/api/v1/pets/{petId}/status", new { status = "Deceased" });
        noRowVersion.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var readOnly = await SeedAsync([PermissionCatalog.Pets.Read]);
        var forbidden = await readOnly.Http.PutAsJsonAsync($"/api/v1/pets/{petId}/status", new { status = "Deceased", rowVersion = rv });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await ctx.GetDetailAsync(petId)).Status.Should().Be(PetStatus.Active);
    }

    [Fact]
    public async Task Pet_Put_Should_Not_Touch_Status()
    {
        var ctx = await SeedAsync();
        var petId = await ctx.CreatePetAsync("PutPet");
        var rv = (await ctx.GetDetailAsync(petId)).RowVersion;
        (await ctx.Http.PutAsJsonAsync($"/api/v1/pets/{petId}/status", new { status = "Deceased", rowVersion = rv }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var put = await ctx.Http.PutAsJsonAsync($"/api/v1/pets/{petId}", new
        {
            id = petId, clientId = ctx.ClientId, name = "PutPet2", speciesId = ctx.SpeciesId,
        });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent, await put.Content.ReadAsStringAsync());

        var detail = await ctx.GetDetailAsync(petId);
        detail.Name.Should().Be("PutPet2");
        detail.Status.Should().Be(PetStatus.Deceased);
    }

    [Fact]
    public async Task Client_ContactPreferences_Should_Roundtrip_And_Survive_Put_Without_Fields()
    {
        var ctx = await SeedAsync();

        var create = await ctx.Http.PostAsJsonAsync("/api/v1/clients", new
        {
            fullName = $"Tercih-{Guid.NewGuid():N}"[..14],
            phone = "05551110081",
            preferredContactChannel = "WhatsApp",
            remindersOptedOut = true,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var clientId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var detail = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{clientId}");
        detail.GetProperty("preferredContactChannel").GetString().Should().Be("WhatsApp");
        detail.GetProperty("remindersOptedOut").GetBoolean().Should().BeTrue();
        var fullName = detail.GetProperty("fullName").GetString();

        // Alanı bilmeyen eski istemci: tercihler korunur.
        (await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{clientId}", new { fullName, phone = "05551110081", address = "Izmir" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var kept = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{clientId}");
        kept.GetProperty("preferredContactChannel").GetString().Should().Be("WhatsApp");
        kept.GetProperty("remindersOptedOut").GetBoolean().Should().BeTrue();

        // Açık değerle temizleme.
        (await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{clientId}",
                new { fullName, phone = "05551110081", preferredContactChannel = "None", remindersOptedOut = false }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var cleared = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{clientId}");
        cleared.GetProperty("preferredContactChannel").GetString().Should().Be("None");
        cleared.GetProperty("remindersOptedOut").GetBoolean().Should().BeFalse();

        var invalid = await ctx.Http.PutAsJsonAsync($"/api/v1/clients/{clientId}",
            new { fullName, preferredContactChannel = "Fax" });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Kurulum ----

    private async Task<StatusTestContext> SeedAsync(string[]? permissions = null)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid clinicId;
        Guid clientId;
        Guid speciesId;
        string email;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Status-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            var client = new Client(tenant.Id, $"SClient-{Guid.NewGuid():N}"[..14], "905551110082");
            db.Clients.Add(client);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"status-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"));
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id));
            await db.SaveChangesAsync();

            clinicId = clinic.Id;
            clientId = client.Id;
            speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, permissions ?? AllPermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new StatusTestContext(http, clinicId, clientId, speciesId);
    }

    private sealed class StatusTestContext(HttpClient http, Guid clinicId, Guid clientId, Guid speciesId)
    {
        public HttpClient Http { get; } = http;
        public Guid ClinicId { get; } = clinicId;
        public Guid ClientId { get; } = clientId;
        public Guid SpeciesId { get; } = speciesId;

        public async Task<Guid> CreatePetAsync(string name)
        {
            var response = await Http.PostAsJsonAsync("/api/v1/pets", new { clientId = ClientId, name, speciesId = SpeciesId });
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<Guid>();
        }

        public async Task<PetDetailDto> GetDetailAsync(Guid petId)
            => (await Http.GetFromJsonAsync<PetDetailDto>($"/api/v1/pets/{petId}"))!;

        public async Task<List<Guid>> ListPetIdsAsync(string? status)
        {
            var url = $"/api/v1/pets?clientId={ClientId}&pageSize=50" + (status is null ? string.Empty : $"&status={status}");
            var page = await Http.GetFromJsonAsync<JsonElement>(url);
            return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        }
    }
}
