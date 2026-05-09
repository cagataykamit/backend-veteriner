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

namespace Backend.IntegrationTests.Products;

[Collection("products-api")]
public sealed class ProductCategoriesEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductCategoriesEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetList_Should_Return401_When_TokenMissing()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/product-categories");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetList_Should_Return403_When_MissingPermission()
    {
        var client = _factory.CreateClient();
        var (tenantId, token) = await SeedTenantAndIssueTokenAsync(new[] { "Permissions.Read" });
        token.Should().NotBeNullOrWhiteSpace();
        tenantId.Should().NotBe(Guid.Empty);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/v1/product-categories");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Read_Create_Duplicate_Detail_UpdateMismatch_ActivateDeactivate_Flow_Should_Work()
    {
        var client = _factory.CreateClient();
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[]
        {
            "ProductCategories.Read",
            "ProductCategories.Create",
            "ProductCategories.Update",
            "ProductCategories.Deactivate"
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createBody = new { name = $"Ilac-{Guid.NewGuid():N}"[..13], description = "Kategori Aciklama" };

        var create = await client.PostAsJsonAsync("/api/v1/product-categories", createBody);
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("name").GetString().Should().Be(createBody.name);
        created.GetProperty("description").GetString().Should().Be(createBody.description);
        created.GetProperty("isActive").GetBoolean().Should().BeTrue();

        var list = await client.GetAsync("/api/v1/product-categories?page=1&pageSize=20&search=Ilac");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        var duplicate = await client.PostAsJsonAsync("/api/v1/product-categories", createBody);
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(duplicate)).Should().Be("ProductCategories.NameAlreadyExists");

        var mismatch = await client.PutAsJsonAsync($"/api/v1/product-categories/{id}",
            new { id = Guid.NewGuid(), name = "Mismatch", description = "x" });
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(mismatch)).Should().Be("ProductCategories.RouteIdMismatch");

        var deactivate = await client.PostAsync($"/api/v1/product-categories/{id}/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var activate = await client.PostAsync($"/api/v1/product-categories/{id}/activate", null);
        activate.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await client.GetAsync($"/api/v1/product-categories/{id}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Detail_Should_ReturnNotFound_For_OtherTenant_Category()
    {
        var ownerClient = _factory.CreateClient();
        var (_, ownerToken) = await SeedTenantAndIssueTokenAsync(new[] { "ProductCategories.Read", "ProductCategories.Create" });
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var created = await ownerClient.PostAsJsonAsync("/api/v1/product-categories", new { name = $"Own-{Guid.NewGuid():N}"[..13] });
        var categoryId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var otherClient = _factory.CreateClient();
        var (_, otherToken) = await SeedTenantAndIssueTokenAsync(new[] { "ProductCategories.Read" });
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var response = await otherClient.GetAsync($"/api/v1/product-categories/{categoryId}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemCodeAsync(response)).Should().Be("ProductCategories.NotFound");
    }

    [Fact]
    public async Task Write_Should_Be_Blocked_For_ReadOnlyTenant()
    {
        var client = _factory.CreateClient();
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[] { "ProductCategories.Create" }, readOnlyTenant: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/product-categories", new { name = $"RO-{Guid.NewGuid():N}"[..13] });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemCodeAsync(response)).Should().Be("Subscriptions.TenantReadOnly");
    }

    private async Task<(Guid TenantId, string AccessToken)> SeedTenantAndIssueTokenAsync(IReadOnlyCollection<string> permissions, bool readOnlyTenant = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);

        var now = DateTime.UtcNow;
        var subscription = readOnlyTenant
            ? TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now.AddDays(-40), 7)
            : TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, 14);
        db.TenantSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var claims = permissions
            .Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D")))
            .ToList();

        var (accessToken, _, _) = jwt.Create(Guid.NewGuid(), $"it-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return (tenant.Id, accessToken);
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (json.TryGetProperty("code", out var code))
            return code.GetString();
        if (json.TryGetProperty("extensions", out var ext)
            && ext.TryGetProperty("code", out var extCode))
            return extCode.GetString();
        return null;
    }
}
