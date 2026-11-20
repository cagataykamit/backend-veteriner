using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Vaccinations;

/// <summary>
/// Panelden tarih ve saatle girilen vade saatli kalır (APPOINTMENT_CALENDAR_API_CONTRACT.md §8.1, 2026-11-20): <c>dueTimeSpecified</c>
/// HTTP gövdesinden kayda ve takvim satırının <c>timeSpecified</c> alanına geçer; verilmezse eski davranış sürer.
/// </summary>
[Collection("products-api")]
public sealed class VaccinationDueTimeIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] Permissions = ["Vaccinations.Create", "Vaccinations.Update", "Vaccinations.Read", "Pets.Read"];

    private readonly CustomWebApplicationFactory _factory;

    public VaccinationDueTimeIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Scheduled_Vaccination_With_Time_Should_Be_Time_Specified_And_Without_Flag_Not()
    {
        var ctx = await SeedAsync();
        var http = Client(ctx);
        var due = DateTime.UtcNow.Date.AddDays(3).AddHours(11).AddMinutes(30);

        var withTime = await CreateScheduledAsync(http, ctx, due, dueTimeSpecified: true);
        var withoutFlag = await CreateScheduledAsync(http, ctx, due, dueTimeSpecified: null);

        (await FlagAsync(withTime)).Should().BeTrue();
        (await FlagAsync(withoutFlag)).Should().BeFalse("eski istemci ve gün bazlı plan saati bildirmez");
    }

    [Fact]
    public async Task Update_Should_Keep_Flag_For_Unchanged_Due_And_Set_It_When_Panel_Sends_New_Time()
    {
        var ctx = await SeedAsync();
        var http = Client(ctx);
        var due = DateTime.UtcNow.Date.AddDays(3).AddHours(8);
        var id = await CreateScheduledAsync(http, ctx, due, dueTimeSpecified: true);

        (await PutAsync(http, ctx, id, due, notes: "yalnız not", dueTimeSpecified: null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FlagAsync(id)).Should().BeTrue("vade değişmedi");

        (await PutAsync(http, ctx, id, due.AddDays(1), notes: null, dueTimeSpecified: null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FlagAsync(id)).Should().BeFalse("bildirilmeyen yeni vade saatsiz sayılır");

        (await PutAsync(http, ctx, id, due.AddDays(1).AddHours(2), notes: null, dueTimeSpecified: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FlagAsync(id)).Should().BeTrue("panelde saatiyle değiştirildi");
    }

    private static async Task<Guid> CreateScheduledAsync(HttpClient http, SeedCtx ctx, DateTime dueAtUtc, bool? dueTimeSpecified)
    {
        object body = dueTimeSpecified is { } flag
            ? new { clinicId = ctx.ClinicId, petId = ctx.PetId, vaccineDefinitionId = ctx.VaccineDefinitionId, status = VaccinationStatus.Scheduled, dueAtUtc, dueTimeSpecified = flag }
            : new { clinicId = ctx.ClinicId, petId = ctx.PetId, vaccineDefinitionId = ctx.VaccineDefinitionId, status = VaccinationStatus.Scheduled, dueAtUtc };
        var response = await http.PostAsJsonAsync("/api/v1/vaccinations", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        // Ürünsüz aşıda yanıt yalın kimliktir.
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient http, SeedCtx ctx, Guid id, DateTime dueAtUtc, string? notes, bool? dueTimeSpecified)
    {
        var rowVersion = (await (await http.GetAsync($"/api/v1/vaccinations/{id:D}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rowVersion").GetString();
        object body = dueTimeSpecified is { } flag
            ? new { clinicId = ctx.ClinicId, petId = ctx.PetId, vaccineDefinitionId = ctx.VaccineDefinitionId, status = VaccinationStatus.Scheduled, dueAtUtc, notes, rowVersion, dueTimeSpecified = flag }
            : new { clinicId = ctx.ClinicId, petId = ctx.PetId, vaccineDefinitionId = ctx.VaccineDefinitionId, status = VaccinationStatus.Scheduled, dueAtUtc, notes, rowVersion };
        return await http.PutAsJsonAsync($"/api/v1/vaccinations/{id:D}", body);
    }

    private async Task<bool> FlagAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Vaccinations.AsNoTracking().Where(v => v.Id == id).Select(v => v.DueTimeSpecified).SingleAsync();
    }

    private HttpClient Client(SeedCtx ctx)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        return client;
    }

    private async Task<SeedCtx> SeedAsync()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"vdt-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Vdt-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var client = new Client(tenant.Id, $"VdtClient-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110098");
        db.Clients.Add(client);
        var definition = VaccineDefinition.CreateGlobal($"D{Guid.NewGuid():N}"[..8], "Saat testi aşısı", DateTime.UtcNow);
        db.VaccineDefinitions.Add(definition);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, $"VdtPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, Permissions, includeClinicClaimInJwt: true);

        return new SeedCtx(clinic.Id, pet.Id, definition.Id, token);
    }

    private sealed record SeedCtx(Guid ClinicId, Guid PetId, Guid VaccineDefinitionId, string Token);
}
