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
public sealed class SpeciesEndpointPaginationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SpeciesEndpointPaginationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetList_Page2_PageSize10_Should_ReturnPageTwo_WithDifferentItemsThanPage1()
    {
        var client = _factory.CreateClient();
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[] { "Species.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var page1 = await GetPagedJsonAsync(client, "/api/v1/species?Page=1&PageSize=10");
        var page2 = await GetPagedJsonAsync(client, "/api/v1/species?Page=2&PageSize=10");

        page2.GetProperty("page").GetInt32().Should().Be(2);
        page2.GetProperty("pageSize").GetInt32().Should().Be(10);
        page2.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);

        var page1Ids = page1.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToHashSet();
        var page2Ids = page2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToHashSet();
        page2Ids.Should().NotBeEquivalentTo(page1Ids);
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
