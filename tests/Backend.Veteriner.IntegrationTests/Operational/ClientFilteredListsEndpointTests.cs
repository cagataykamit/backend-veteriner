using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Operational;

/// <summary>
/// Randevu/muayene/aşı listelerinde <c>clientId</c> süzgeci: müşteri A'nın kayıtları B'ninkilerden ayrılır,
/// başka kiracının müşteri kimliği boş sayfa döner (kiracı yalıtımı).
/// </summary>
[Collection("products-api")]
public sealed class ClientFilteredListsEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ClientFilteredListsEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    public static TheoryData<string, string> Lists => new()
    {
        { "appointments", PermissionCatalog.Appointments.Read },
        { "examinations", PermissionCatalog.Examinations.Read },
        { "vaccinations", PermissionCatalog.Vaccinations.Read },
    };

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task ClientId_Should_Return_Only_That_Clients_Records(string list, string permission)
    {
        var ctx = await SeedAsync(permission);
        var http = CreateClient(ctx.Token);

        var forA = await GetIdsAsync(http, $"/api/v1/{list}?page=1&pageSize=50&clinicId={ctx.ClinicId:D}&clientId={ctx.ClientAId:D}");
        var forB = await GetIdsAsync(http, $"/api/v1/{list}?page=1&pageSize=50&clinicId={ctx.ClinicId:D}&clientId={ctx.ClientBId:D}");
        var all = await GetIdsAsync(http, $"/api/v1/{list}?page=1&pageSize=50&clinicId={ctx.ClinicId:D}");

        forA.Should().BeEquivalentTo(ctx.RecordIdsA[list]);
        forB.Should().BeEquivalentTo(ctx.RecordIdsB[list]);
        all.Should().Contain(ctx.RecordIdsA[list]).And.Contain(ctx.RecordIdsB[list]);
    }

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task ClientId_Should_Combine_With_PetId_And_Search_As_And(string list, string permission)
    {
        var ctx = await SeedAsync(permission);
        var http = CreateClient(ctx.Token);

        var mismatch = await GetIdsAsync(http, $"/api/v1/{list}?page=1&pageSize=50&clinicId={ctx.ClinicId:D}&clientId={ctx.ClientAId:D}&petId={ctx.PetBId:D}");
        var searchOtherClient = await GetIdsAsync(http, $"/api/v1/{list}?page=1&pageSize=50&clinicId={ctx.ClinicId:D}&clientId={ctx.ClientAId:D}&search={Uri.EscapeDataString(ctx.PetBName)}");

        mismatch.Should().BeEmpty();
        searchOtherClient.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task ClientId_From_Other_Tenant_Should_Return_Empty_Page(string list, string permission)
    {
        var ctx = await SeedAsync(permission);
        var other = await SeedAsync(permission);
        var http = CreateClient(ctx.Token);

        var response = await http.GetAsync($"/api/v1/{list}?page=1&pageSize=50&clinicId={ctx.ClinicId:D}&clientId={other.ClientAId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalItems").GetInt32().Should().Be(0);
        json.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task Empty_ClientId_Should_Return_400(string list, string permission)
    {
        var ctx = await SeedAsync(permission);
        var http = CreateClient(ctx.Token);

        var response = await http.GetAsync($"/api/v1/{list}?page=1&pageSize=50&clinicId={ctx.ClinicId:D}&clientId={Guid.Empty:D}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private HttpClient CreateClient(string token)
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<List<Guid>> GetIdsAsync(HttpClient http, string url)
    {
        var response = await http.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = json.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        json.GetProperty("totalItems").GetInt32().Should().Be(ids.Count, url);
        return ids;
    }

    private sealed record SeedCtx(
        string Token,
        Guid ClinicId,
        Guid ClientAId,
        Guid ClientBId,
        Guid PetBId,
        string PetBName,
        Dictionary<string, Guid[]> RecordIdsA,
        Dictionary<string, Guid[]> RecordIdsB);

    private async Task<SeedCtx> SeedAsync(string permission)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = new Tenant($"CliF-{suffix}");
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var clientA = new Client(tenant.Id, $"Musteri A {suffix}", "905551112201");
        var clientB = new Client(tenant.Id, $"Musteri B {suffix}", "905551112202");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var petA = new Pet(tenant.Id, clientA.Id, $"PetA{suffix}", speciesId);
        var petA2 = new Pet(tenant.Id, clientA.Id, $"PetAiki{suffix}", speciesId);
        var petB = new Pet(tenant.Id, clientB.Id, $"PetB{suffix}", speciesId);
        var vaccineDefinitionId = await db.VaccineDefinitions.AsNoTracking()
            .Where(v => v.TenantId == null && v.Code == "RABIES")
            .Select(v => v.Id)
            .FirstAsync();

        var now = DateTime.UtcNow;
        Appointment Appt(Pet pet, int days) => new(tenant.Id, clinic.Id, pet.Id, now.Date.AddDays(days).AddHours(10));
        Examination Exam(Pet pet, int hours) => new(tenant.Id, clinic.Id, pet.Id, null, now.AddHours(-hours), "Kontrol", "Bulgu", null, null);
        Vaccination Vacc(Pet pet, int days) => new(tenant.Id, pet.Id, clinic.Id, null, vaccineDefinitionId, "Kuduz",
            VaccinationStatus.Scheduled, null, now.AddDays(days), null);

        var apptsA = new[] { Appt(petA, 1), Appt(petA2, 2) };
        var apptsB = new[] { Appt(petB, 3) };
        var examsA = new[] { Exam(petA, 1), Exam(petA2, 2) };
        var examsB = new[] { Exam(petB, 3) };
        var vaccsA = new[] { Vacc(petA, 10), Vacc(petA2, 20) };
        var vaccsB = new[] { Vacc(petB, 30) };

        db.AddRange(tenant, clinic, clientA, clientB, petA, petA2, petB);
        db.AddRange(apptsA);
        db.AddRange(apptsB);
        db.AddRange(examsA);
        db.AddRange(examsB);
        db.AddRange(vaccsA);
        db.AddRange(vaccsB);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var token = await IntegrationTestAuthHelper.SeedScopedListReaderAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, permission);

        return new SeedCtx(
            token,
            clinic.Id,
            clientA.Id,
            clientB.Id,
            petB.Id,
            petB.Name,
            new Dictionary<string, Guid[]>
            {
                ["appointments"] = apptsA.Select(x => x.Id).ToArray(),
                ["examinations"] = examsA.Select(x => x.Id).ToArray(),
                ["vaccinations"] = vaccsA.Select(x => x.Id).ToArray(),
            },
            new Dictionary<string, Guid[]>
            {
                ["appointments"] = apptsB.Select(x => x.Id).ToArray(),
                ["examinations"] = examsB.Select(x => x.Id).ToArray(),
                ["vaccinations"] = vaccsB.Select(x => x.Id).ToArray(),
            });
    }
}
