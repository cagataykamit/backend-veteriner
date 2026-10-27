using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Catalog;

[Collection("clinical-terms-api")]
public sealed class ClinicalTermsEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Url = "/api/v1/clinical-terms";

    private static readonly string[] AllPermissions =
    [
        "ClinicalTerms.Read",
        "ClinicalTerms.Create",
        "ClinicalTerms.Update",
        "ClinicalTerms.Deactivate",
    ];

    private readonly CustomWebApplicationFactory _factory;

    public ClinicalTermsEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ClinicalTermSeeder.SeedAsync(db).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task GetList_Should_Return401_Without_Token_And_403_Without_Permission()
    {
        var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var client = await ClientAsync(["Permissions.Read"]);
        (await client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Read_Only_Role_Should_Not_Create()
    {
        var client = await ClientAsync(["ClinicalTerms.Read"]);

        var response = await client.PostAsJsonAsync(Url, new { kind = "Complaint", name = "Deneme" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_Should_Return_System_Terms_With_Turkish_Insensitive_Search()
    {
        var client = await ClientAsync(AllPermissions);

        var page = await GetPageAsync(client, "?kind=Complaint&search=ISHAL");

        var names = Names(page);
        names.Should().Contain("İshal");
        page.GetProperty("items").EnumerateArray().Should().OnlyContain(i => i.GetProperty("kind").GetString() == "Complaint");
        var ishal = page.GetProperty("items").EnumerateArray().First(i => i.GetProperty("name").GetString() == "İshal");
        ishal.GetProperty("isSystem").GetBoolean().Should().BeTrue();
        ishal.GetProperty("rowVersion").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task List_Should_Put_Prefix_Matches_First_And_Match_All_Words()
    {
        var client = await ClientAsync(AllPermissions);
        var create = await client.PostAsJsonAsync(Url, new { kind = "Complaint", name = "Kanlı ishal" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var byPrefix = await GetPageAsync(client, "?kind=Complaint&search=ishal");
        Names(byPrefix).First().Should().Be("İshal");
        Names(byPrefix).Should().Contain("Kanlı ishal");

        var byWords = await GetPageAsync(client, "?kind=Complaint&search=ISHAL%20kanli");
        Names(byWords).Should().Equal("Kanlı ishal");
    }

    [Fact]
    public async Task Create_Update_Conflict_Deactivate_Activate_Flow_Should_Work()
    {
        var client = await ClientAsync(AllPermissions);
        var name = $"Kulak akıntısı {Guid.NewGuid():N}"[..24];

        var create = await client.PostAsJsonAsync(Url, new { kind = "Diagnosis", name = $"  {name}  " });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("name").GetString().Should().Be(name);
        created.GetProperty("kind").GetString().Should().Be("Diagnosis");
        created.GetProperty("isSystem").GetBoolean().Should().BeFalse();
        var firstVersion = created.GetProperty("rowVersion").GetString();

        var duplicate = await client.PostAsJsonAsync(Url, new { kind = "Diagnosis", name = name.ToUpper(new System.Globalization.CultureInfo("tr-TR")) });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(duplicate)).Should().Be("ClinicalTerms.DuplicateName");

        // Aynı ad diğer türde serbesttir.
        (await client.PostAsJsonAsync(Url, new { kind = "Complaint", name })).StatusCode.Should().Be(HttpStatusCode.Created);

        var renamed = $"{name} (sol)";
        var update = await client.PutAsJsonAsync($"{Url}/{id}", new { name = renamed, rowVersion = firstVersion });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("name").GetString().Should().Be(renamed);
        updated.GetProperty("rowVersion").GetString().Should().NotBe(firstVersion);

        var stale = await client.PutAsJsonAsync($"{Url}/{id}", new { name = $"{name} (sağ)", rowVersion = firstVersion });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(stale)).Should().Be("ClinicalTerms.ConcurrencyConflict");

        var noVersion = await client.PutAsJsonAsync($"{Url}/{id}", new { name = renamed });
        noVersion.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var mismatch = await client.PutAsJsonAsync($"{Url}/{id}", new { id = Guid.NewGuid(), name = renamed, rowVersion = firstVersion });
        (await ProblemCodeAsync(mismatch)).Should().Be("ClinicalTerms.RouteIdMismatch");

        (await client.PostAsync($"{Url}/{id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        Names(await GetPageAsync(client, $"?kind=Diagnosis&search={Uri.EscapeDataString(renamed)}")).Should().BeEmpty();
        var withInactive = await GetPageAsync(client, $"?kind=Diagnosis&includeInactive=true&search={Uri.EscapeDataString(renamed)}");
        withInactive.GetProperty("items")[0].GetProperty("isActive").GetBoolean().Should().BeFalse();

        // Pasif kayıtla aynı ad yeniden eklenemez (etkinleştirilir).
        var duplicateOfInactive = await client.PostAsJsonAsync(Url, new { kind = "Diagnosis", name = renamed });
        duplicateOfInactive.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.PostAsync($"{Url}/{id}/activate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var detail = await client.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        detail.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task System_Terms_Should_Be_Read_Only_And_Block_Same_Name()
    {
        var client = await ClientAsync(AllPermissions);
        var system = (await GetPageAsync(client, "?kind=Diagnosis&search=gastroenterit")).GetProperty("items")
            .EnumerateArray().First(i => i.GetProperty("name").GetString() == "Gastroenterit");
        var id = system.GetProperty("id").GetGuid();

        var update = await client.PutAsJsonAsync($"{Url}/{id}", new { name = "Gastroenterit X", rowVersion = system.GetProperty("rowVersion").GetString() });
        update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(update)).Should().Be("ClinicalTerms.SystemTermReadOnly");

        var deactivate = await client.PostAsync($"{Url}/{id}/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(deactivate)).Should().Be("ClinicalTerms.SystemTermReadOnly");

        var sameName = await client.PostAsJsonAsync(Url, new { kind = "Diagnosis", name = "GASTROENTERİT" });
        sameName.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Tenant_Terms_Should_Be_Isolated()
    {
        var owner = await ClientAsync(AllPermissions);
        var name = $"Özel tanı {Guid.NewGuid():N}"[..20];
        var create = await owner.PostAsJsonAsync(Url, new { kind = "Diagnosis", name });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var other = await ClientAsync(AllPermissions);
        var detail = await other.GetAsync($"{Url}/{id}");
        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(detail)).Should().Be("ClinicalTerms.NotFound");
        Names(await GetPageAsync(other, $"?search={Uri.EscapeDataString(name)}")).Should().BeEmpty();
        (await other.PostAsync($"{Url}/{id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Diğer kiracı aynı adı kendi listesine ekleyebilir.
        (await other.PostAsJsonAsync(Url, new { kind = "Diagnosis", name })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Seeder_Should_Be_Idempotent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.ClinicalTerms.CountAsync(t => t.TenantId == null);

        await ClinicalTermSeeder.SeedAsync(db);

        (await db.ClinicalTerms.CountAsync(t => t.TenantId == null)).Should().Be(before);
        before.Should().Be(ClinicalTermSeeder.Complaints.Count + ClinicalTermSeeder.Diagnoses.Count);
    }

    private async Task<HttpClient> ClientAsync(IReadOnlyCollection<string> permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        await db.SaveChangesAsync();

        var claims = permissions
            .Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D")))
            .ToList();
        var (accessToken, _, _) = jwt.Create(Guid.NewGuid(), $"it-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static async Task<JsonElement> GetPageAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync(Url + query);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<string?> Names(JsonElement page)
        => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList();

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
