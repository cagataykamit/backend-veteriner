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
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Veteriner.IntegrationTests.Catalog;

[Collection("products-api")]
public sealed class BreedsEndpointPaginationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public BreedsEndpointPaginationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetList_Page1_PageSize20_Should_ReturnFirstPageMetadata()
    {
        var client = await CreateAuthorizedClientAsync();
        var json = await GetPagedJsonAsync(client, "/api/v1/breeds?Page=1&PageSize=20");

        json.GetProperty("page").GetInt32().Should().Be(1);
        json.GetProperty("pageSize").GetInt32().Should().Be(20);
        json.GetProperty("totalItems").GetInt32().Should().BeGreaterThanOrEqualTo(21);
        json.GetProperty("items").GetArrayLength().Should().Be(20);
    }

    [Fact]
    public async Task GetList_Page2_PageSize20_Should_ReturnDifferentItemsThanPage1()
    {
        var client = await CreateAuthorizedClientAsync();
        var page1 = await GetPagedJsonAsync(client, "/api/v1/breeds?Page=1&PageSize=20");
        var page2 = await GetPagedJsonAsync(client, "/api/v1/breeds?Page=2&PageSize=20");

        page2.GetProperty("page").GetInt32().Should().Be(2);
        page2.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);

        var page1Ids = page1.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToHashSet();
        var page2Ids = page2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToHashSet();

        page2Ids.Should().NotBeEquivalentTo(page1Ids);
        page2Ids.Overlaps(page1Ids).Should().BeFalse();
        page2.GetProperty("items")[0].GetProperty("id").GetGuid().Should().NotBe(page1.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task GetList_Page1_PageSize500_Should_ClampPageSize_And_ReturnUpToTotalItems()
    {
        var client = await CreateAuthorizedClientAsync();
        var json = await GetPagedJsonAsync(client, "/api/v1/breeds?Page=1&PageSize=500");

        var totalItems = json.GetProperty("totalItems").GetInt32();
        totalItems.Should().BeGreaterThanOrEqualTo(21);

        json.GetProperty("pageSize").GetInt32().Should().Be(200);
        json.GetProperty("items").GetArrayLength().Should().Be(Math.Min(totalItems, 200));
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        var client = _factory.CreateClient();
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[] { "Breeds.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> GetPagedJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>())!;
    }

    private async Task<(Guid TenantId, string AccessToken)> SeedTenantAndIssueTokenAsync(
        IReadOnlyCollection<string> permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(
            TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        await db.SaveChangesAsync();

        var claims = permissions
            .Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D")))
            .ToList();

        var (accessToken, _, _) = jwt.Create(Guid.NewGuid(), $"it-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return (tenant.Id, accessToken);
    }
}
