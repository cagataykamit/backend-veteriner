using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Pets;

/// <summary>CHECKIN-013 hayvan uyarıları: HTTP + gerçek SQL Server (LocalDB). Kısmi güncelleme, okuma, Bugün satırı, izolasyon.</summary>
[Collection("pilot-smoke-api")]
public sealed class PetAlertsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AllPermissions =
    [
        PermissionCatalog.Pets.Read,
        PermissionCatalog.Pets.Create,
        PermissionCatalog.Pets.Update,
        PermissionCatalog.Visits.Read,
        PermissionCatalog.Visits.Create,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public PetAlertsIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_Without_Alerts_Should_Default_To_No_Alerts_And_Detail_Never_Returns_Null_PetAlerts()
    {
        var ctx = await SeedAsync();

        var petId = await ctx.CreatePetAsync(new { });
        var detail = await ctx.GetDetailAsync(petId);

        detail.PetAlerts.Should().NotBeNull();
        detail.PetAlerts.Flags.Should().BeEmpty();
        detail.PetAlerts.Note.Should().BeNull();
    }

    [Fact]
    public async Task Create_With_Alerts_Should_Persist_And_Return_Flags_In_Fixed_Order()
    {
        var ctx = await SeedAsync();

        var petId = await ctx.CreatePetAsync(new { alertFlags = new[] { "Aggressive", "Allergy" }, alertNote = "Penisiline alerji" });
        var detail = await ctx.GetDetailAsync(petId);

        detail.PetAlerts.Flags.Should().Equal("Allergy", "Aggressive");
        detail.PetAlerts.Note.Should().Be("Penisiline alerji");
    }

    [Fact]
    public async Task Put_Without_Alert_Fields_Should_Not_Touch_Existing_Alerts()
    {
        var ctx = await SeedAsync();
        var petId = await ctx.CreatePetAsync(new { alertFlags = new[] { "ChronicCondition" }, alertNote = "Diyabet" });

        // Alanı bilmeyen eski istemci: tam değiştirme PUT'u, uyarı alanları yok.
        var put = await ctx.PutPetAsync(petId, new { name = "Yeni Ad" });

        put.StatusCode.Should().Be(HttpStatusCode.NoContent, await put.Content.ReadAsStringAsync());
        var detail = await ctx.GetDetailAsync(petId);
        detail.Name.Should().Be("Yeni Ad");
        detail.PetAlerts.Flags.Should().Equal("ChronicCondition");
        detail.PetAlerts.Note.Should().Be("Diyabet");
    }

    [Fact]
    public async Task Put_Should_Set_Replace_And_Clear_Alerts_With_Explicit_Values()
    {
        var ctx = await SeedAsync();
        var petId = await ctx.CreatePetAsync(new { });

        (await ctx.PutPetAsync(petId, new { alertFlags = new[] { "Allergy", "AnesthesiaRisk" }, alertNote = "Lateks" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ctx.GetDetailAsync(petId)).PetAlerts.Flags.Should().Equal("Allergy", "AnesthesiaRisk");

        // Yalnızca not: bayraklar korunur.
        (await ctx.PutPetAsync(petId, new { alertNote = "Lateks ve iyot" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var noteOnly = await ctx.GetDetailAsync(petId);
        noteOnly.PetAlerts.Flags.Should().Equal("Allergy", "AnesthesiaRisk");
        noteOnly.PetAlerts.Note.Should().Be("Lateks ve iyot");

        // Boş liste: tüm bayraklar ve not temizlenir.
        (await ctx.PutPetAsync(petId, new { alertFlags = Array.Empty<string>() })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var cleared = await ctx.GetDetailAsync(petId);
        cleared.PetAlerts.Flags.Should().BeEmpty();
        cleared.PetAlerts.Note.Should().BeNull();
    }

    [Fact]
    public async Task Invalid_Alerts_Should_Return_400_And_Not_Change_Pet()
    {
        var ctx = await SeedAsync();
        var petId = await ctx.CreatePetAsync(new { alertFlags = new[] { "Allergy" }, alertNote = "Not" });

        var unknown = await ctx.PutPetAsync(petId, new { name = "Degisti", alertFlags = new[] { "Telepathy" } });
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(unknown)).Should().Be("Pets.Validation");

        var tooLong = await ctx.PutPetAsync(petId, new { alertNote = new string('x', 201) });
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var noteWithoutFlags = await ctx.PutPetAsync(petId, new { alertFlags = Array.Empty<string>(), alertNote = "Yetim not" });
        noteWithoutFlags.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noteWithoutFlags)).Should().Be("Pets.Validation");

        var detail = await ctx.GetDetailAsync(petId);
        detail.Name.Should().Be("AlertPet");
        detail.PetAlerts.Flags.Should().Equal("Allergy");

        var createUnknown = await ctx.Http.PostAsJsonAsync("/api/v1/pets", ctx.PetBody(new { alertFlags = new[] { "Nope" } }, "BaskaAd"));
        createUnknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Alerts_Write_Should_Require_Pets_Update_Permission()
    {
        var ctx = await SeedAsync([PermissionCatalog.Pets.Read, PermissionCatalog.Pets.Create]);
        var petId = await ctx.CreatePetAsync(new { });

        var put = await ctx.PutPetAsync(petId, new { alertFlags = new[] { "Allergy" } });

        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Today_Should_Carry_PetAlerts_For_Visit_And_Planned_Rows_And_Empty_Alerts_Otherwise()
    {
        var ctx = await SeedAsync();
        var alertPet = await ctx.CreatePetAsync(new { alertFlags = new[] { "Aggressive" }, alertNote = "Isirir" });
        var plainPet = await ctx.CreatePetAsync(new { }, name: "PlainPet");
        var plannedPet = await ctx.CreatePetAsync(new { alertFlags = new[] { "Allergy" } }, name: "PlannedPet");
        (await ctx.Http.PostAsJsonAsync("/api/v1/visits", new { clinicId = ctx.ClinicId, petId = alertPet }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await ctx.Http.PostAsJsonAsync("/api/v1/visits", new { clinicId = ctx.ClinicId, petId = plainPet }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        await ctx.SeedAppointmentAsync(plannedPet);

        var today = await ctx.Http.GetFromJsonAsync<TodayDto>($"/api/v1/visits/today?clinicId={ctx.ClinicId}");

        today!.Items.Should().HaveCount(3);
        today.Items.Single(i => i.PetId == alertPet).PetAlerts.Should().BeEquivalentTo(new PetAlertsDto(["Aggressive"], "Isirir"));
        today.Items.Single(i => i.PetId == plainPet).PetAlerts.Should().BeEquivalentTo(new PetAlertsDto([], null));
        var planned = today.Items.Single(i => i.PetId == plannedPet);
        planned.VisitId.Should().BeNull();
        planned.PetAlerts.Flags.Should().Equal("Allergy");
    }

    [Fact]
    public async Task Alerts_Should_Not_Cross_Tenants()
    {
        var ctx = await SeedAsync();
        Guid foreignPetId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var speciesId = await db.Species.Select(s => s.Id).FirstAsync();
            var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..20]);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            var client = new Client(tenant.Id, $"Foreign-{Guid.NewGuid():N}"[..14], "905551110069");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var pet = new Pet(tenant.Id, client.Id, $"FPet-{Guid.NewGuid():N}"[..12], speciesId);
            pet.ApplyAlerts(["Allergy"], "Baska kiraci");
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            foreignPetId = pet.Id;
        }

        (await ctx.Http.GetAsync($"/api/v1/pets/{foreignPetId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ctx.PutPetAsync(foreignPetId, new { alertFlags = Array.Empty<string>() }))
            .StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);

        await using var check = _factory.Services.CreateAsyncScope();
        var verifyDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.Pets.AsNoTracking().SingleAsync(p => p.Id == foreignPetId)).AlertFlags.Should().Be(PetAlertFlags.Allergy);
    }

    // ---- Kurulum ----

    private async Task<AlertTestContext> SeedAsync(string[]? permissions = null)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId;
        Guid clinicId;
        Guid clientId;
        Guid speciesId;
        string email;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Alert-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            var client = new Client(tenant.Id, $"AClient-{Guid.NewGuid():N}"[..14], "905551110080");
            db.Clients.Add(client);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"alert-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"));
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id));
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            clinicId = clinic.Id;
            clientId = client.Id;
            speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, permissions ?? AllPermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new AlertTestContext(_factory.Services, http, tenantId, clinicId, clientId, speciesId);
    }

    private sealed class AlertTestContext(
        IServiceProvider services, HttpClient http, Guid tenantId, Guid clinicId, Guid clientId, Guid speciesId)
    {
        public HttpClient Http { get; } = http;
        public Guid ClinicId { get; } = clinicId;

        public Dictionary<string, object?> PetBody(object extra, string name = "AlertPet")
        {
            var body = new Dictionary<string, object?>
            {
                ["clientId"] = clientId,
                ["name"] = name,
                ["speciesId"] = speciesId,
            };
            foreach (var p in extra.GetType().GetProperties())
                body[p.Name] = p.GetValue(extra);
            return body;
        }

        public async Task<Guid> CreatePetAsync(object extra, string name = "AlertPet")
        {
            var response = await Http.PostAsJsonAsync("/api/v1/pets", PetBody(extra, name));
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<Guid>();
        }

        /// <summary>Tam değiştirme PUT'u: zorunlu alanlar (clientId, speciesId, ad) her zaman gönderilir.</summary>
        public Task<HttpResponseMessage> PutPetAsync(Guid petId, object extra)
        {
            var body = new Dictionary<string, object?>
            {
                ["id"] = petId,
                ["clientId"] = clientId,
                ["name"] = "AlertPet",
                ["speciesId"] = speciesId,
            };
            foreach (var p in extra.GetType().GetProperties())
                body[p.Name] = p.GetValue(extra);
            return Http.PutAsJsonAsync($"/api/v1/pets/{petId}", body);
        }

        public async Task<PetDetailDto> GetDetailAsync(Guid petId)
            => (await Http.GetFromJsonAsync<PetDetailDto>($"/api/v1/pets/{petId}"))!;

        public async Task SeedAppointmentAsync(Guid petId)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Appointments.Add(new Appointment(
                tenantId, ClinicId, petId, DateTime.UtcNow.AddMinutes(30), 30, AppointmentType.Consultation));
            await db.SaveChangesAsync();
        }
    }
}
