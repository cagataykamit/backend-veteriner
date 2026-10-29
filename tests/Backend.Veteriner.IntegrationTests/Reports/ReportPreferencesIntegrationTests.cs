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
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Veteriner.IntegrationTests.Reports;

/// <summary>Rapor tercihleri (REPORT_PREFERENCES_API_CONTRACT.md): favoriler kiracı + kullanıcı kapsamında.</summary>
[Collection("products-api")]
public sealed class ReportPreferencesIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ReportPreferencesIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Favorites_Should_Require_Authentication()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/reports/favorites");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Favorites_Should_Add_List_Remove_Idempotently_Per_User()
    {
        var tenantId = await SeedTenantAsync();
        var alice = ClientFor(tenantId, Guid.NewGuid());
        var bob = ClientFor(tenantId, Guid.NewGuid());

        (await alice.PutAsync("/api/v1/reports/favorites/payments", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await alice.PutAsync("/api/v1/reports/favorites/registers.examinations", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await alice.PutAsync("/api/v1/reports/favorites/payments", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await FavoriteKeysAsync(alice)).Should().Equal("payments", "registers.examinations");
        (await FavoriteKeysAsync(bob)).Should().BeEmpty();

        (await alice.DeleteAsync("/api/v1/reports/favorites/payments")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await alice.DeleteAsync("/api/v1/reports/favorites/payments")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FavoriteKeysAsync(alice)).Should().Equal("registers.examinations");
    }

    [Fact]
    public async Task Favorites_Should_Not_Leak_Across_Tenants_For_Same_User()
    {
        var userId = Guid.NewGuid();
        var inTenantA = ClientFor(await SeedTenantAsync(), userId);
        var inTenantB = ClientFor(await SeedTenantAsync(), userId);

        (await inTenantA.PutAsync("/api/v1/reports/favorites/appointments", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await FavoriteKeysAsync(inTenantB)).Should().BeEmpty();
    }

    [Fact]
    public async Task Favorites_Should_Return400_For_Invalid_Key()
    {
        var client = ClientFor(await SeedTenantAsync(), Guid.NewGuid());

        var response = await client.PutAsync("/api/v1/reports/favorites/Bad%20Key", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SavedFilters_Should_Create_List_Delete_Per_User_And_Report()
    {
        var tenantId = await SeedTenantAsync();
        var alice = ClientFor(tenantId, Guid.NewGuid());
        var bob = ClientFor(tenantId, Guid.NewGuid());

        var created = await alice.PostAsJsonAsync("/api/v1/reports/saved-filters", new
        {
            ReportKey = "payments",
            Name = "Nakit tahsilatlar",
            Filters = new Dictionary<string, string> { ["method"] = "Cash", ["search"] = "Pamuk" }
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        body.GetProperty("filters").GetProperty("method").GetString().Should().Be("Cash");

        var mine = await alice.GetFromJsonAsync<JsonElement>("/api/v1/reports/saved-filters?reportKey=payments");
        mine.GetArrayLength().Should().Be(1);
        mine[0].GetProperty("name").GetString().Should().Be("Nakit tahsilatlar");
        mine[0].GetProperty("filters").GetProperty("search").GetString().Should().Be("Pamuk");

        (await alice.GetFromJsonAsync<JsonElement>("/api/v1/reports/saved-filters?reportKey=appointments")).GetArrayLength().Should().Be(0);
        (await bob.GetFromJsonAsync<JsonElement>("/api/v1/reports/saved-filters?reportKey=payments")).GetArrayLength().Should().Be(0);

        (await bob.DeleteAsync($"/api/v1/reports/saved-filters/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await alice.DeleteAsync($"/api/v1/reports/saved-filters/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await alice.GetFromJsonAsync<JsonElement>("/api/v1/reports/saved-filters?reportKey=payments")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task SavedFilters_Should_Return409_For_Same_Name_Ignoring_Turkish_Case()
    {
        var client = ClientFor(await SeedTenantAsync(), Guid.NewGuid());
        var filters = new Dictionary<string, string> { ["status"] = "Completed" };

        (await client.PostAsJsonAsync("/api/v1/reports/saved-filters", new { ReportKey = "appointments", Name = "İptaller", Filters = filters }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var duplicate = await client.PostAsJsonAsync("/api/v1/reports/saved-filters", new { ReportKey = "appointments", Name = "iptaller", Filters = filters });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("ReportSavedFilters.DuplicateName");
    }

    [Fact]
    public async Task SavedFilters_Should_Return400_For_Invalid_Filter_Key()
    {
        var client = ClientFor(await SeedTenantAsync(), Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/api/v1/reports/saved-filters", new
        {
            ReportKey = "payments",
            Name = "Bozuk",
            Filters = new Dictionary<string, string> { ["bad-key"] = "x" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<string[]> FavoriteKeysAsync(HttpClient client)
    {
        var json = await client.GetFromJsonAsync<JsonElement>("/api/v1/reports/favorites");
        return json.EnumerateArray().Select(x => x.GetProperty("reportKey").GetString()!).ToArray();
    }

    private async Task<Guid> SeedTenantAsync()
    {
        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = new Tenant($"Pref-{Guid.NewGuid():N}"[..16], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private HttpClient ClientFor(Guid tenantId, Guid userId)
    {
        using var scope = _factory.Services.CreateSystemScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _, _) = jwt.Create(
            userId,
            $"pref-{Guid.NewGuid():N}@example.com",
            Array.Empty<string>(),
            [new Claim(VeterinerClaims.TenantId, tenantId.ToString("D"))]);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
