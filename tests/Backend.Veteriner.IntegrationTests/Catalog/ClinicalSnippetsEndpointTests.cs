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

namespace Backend.IntegrationTests.Catalog;

/// <summary>EXAM-011/012: klinik metin parçaları (CLINICAL_SNIPPET_API_CONTRACT.md).</summary>
[Collection("clinical-snippets-api")]
public sealed class ClinicalSnippetsEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Url = "/api/v1/clinical-snippets";

    private static readonly string[] AllPermissions =
    [
        "ClinicalSnippets.Read",
        "ClinicalSnippets.Create",
        "ClinicalSnippets.Update",
        "ClinicalSnippets.Deactivate",
    ];

    private readonly CustomWebApplicationFactory _factory;

    public ClinicalSnippetsEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Permissions_Should_Be_Enforced()
    {
        (await _factory.CreateClient().GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var none = await ClientAsync(["Permissions.Read"]);
        (await none.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var readOnly = await ClientAsync(["ClinicalSnippets.Read"]);
        (await readOnly.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await readOnly.PostAsJsonAsync(Url, new { shortcut = "kusma", title = "Kusma", body = "Metin" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_Search_Update_Conflict_Deactivate_Flow_Should_Work()
    {
        var client = await ClientAsync(AllPermissions);

        var create = await client.PostAsJsonAsync(Url, new { shortcut = "#Kusma", title = "Kusma anamnezi", body = "Kaç kez?\nİçerik?" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("shortcut").GetString().Should().Be("kusma");
        created.GetProperty("body").GetString().Should().Be("Kaç kez?\nİçerik?");
        var firstVersion = created.GetProperty("rowVersion").GetString();

        var duplicate = await client.PostAsJsonAsync(Url, new { shortcut = "KUSMA", title = "Başka", body = "Metin" });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(duplicate)).Should().Be("ClinicalSnippets.DuplicateShortcut");

        var invalid = await client.PostAsJsonAsync(Url, new { shortcut = "iki kelime", title = "Başlık", body = "Metin" });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Arama kısayol ve başlıkta, Türkçe duyarsız.
        Shortcuts(await GetPageAsync(client, "?search=ANAMNEZ")).Should().Equal("kusma");
        Shortcuts(await GetPageAsync(client, "?search=kus")).Should().Equal("kusma");

        var update = await client.PutAsJsonAsync($"{Url}/{id}", new { shortcut = "kusma", title = "Kusma", body = "Yeni metin", rowVersion = firstVersion });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await client.PutAsJsonAsync($"{Url}/{id}", new { shortcut = "kusma", title = "Kusma", body = "Eski", rowVersion = firstVersion });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(stale)).Should().Be("ClinicalSnippets.ConcurrencyConflict");

        (await client.PostAsync($"{Url}/{id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        Shortcuts(await GetPageAsync(client, "")).Should().BeEmpty();
        Shortcuts(await GetPageAsync(client, "?includeInactive=true")).Should().Equal("kusma");

        (await client.PostAsync($"{Url}/{id}/activate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var detail = await client.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        detail.GetProperty("isActive").GetBoolean().Should().BeTrue();
        detail.GetProperty("body").GetString().Should().Be("Yeni metin");
    }

    [Fact]
    public async Task Snippets_Should_Be_Tenant_Isolated()
    {
        var owner = await ClientAsync(AllPermissions);
        var create = await owner.PostAsJsonAsync(Url, new { shortcut = "ishal", title = "İshal", body = "Kaç gündür?" });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var other = await ClientAsync(AllPermissions);
        (await other.GetAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        Shortcuts(await GetPageAsync(other, "")).Should().BeEmpty();

        // Aynı kısayol diğer kiracıda serbesttir.
        (await other.PostAsJsonAsync(Url, new { shortcut = "ishal", title = "İshal", body = "Metin" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<HttpClient> ClientAsync(IReadOnlyCollection<string> permissions)
    {
        using var scope = _factory.Services.CreateSystemScope();
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

    private static List<string?> Shortcuts(JsonElement page)
        => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("shortcut").GetString()).ToList();

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
