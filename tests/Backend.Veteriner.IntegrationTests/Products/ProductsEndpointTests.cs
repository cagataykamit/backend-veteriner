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
public sealed class ProductsEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductsEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetList_Should_Return401_When_TokenMissing()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/products");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetList_Should_Return403_When_MissingPermission()
    {
        var client = _factory.CreateClient();
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[] { "Permissions.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/products");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_List_Detail_UpdateMismatch_ActivateDeactivate_Flow_Should_Work()
    {
        var client = _factory.CreateClient();
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[]
        {
            "Products.Read",
            "Products.Create",
            "Products.Update",
            "Products.Deactivate",
            "ProductCategories.Create"
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var category = await client.PostAsJsonAsync("/api/v1/product-categories", new { name = $"Cat-{Guid.NewGuid():N}"[..13] });
        category.StatusCode.Should().Be(HttpStatusCode.Created);
        var categoryId = (await category.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var createBody = new
        {
            productCategoryId = categoryId,
            name = $"Urun-{Guid.NewGuid():N}"[..14],
            sku = $"SKU-{Guid.NewGuid():N}"[..14],
            barcode = "1234567890123",
            description = "desc",
            unit = "Adet",
            unitPrice = 12.5m,
            currency = "TRY"
        };

        var create = await client.PostAsJsonAsync("/api/v1/products", createBody);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("name").GetString().Should().Be(createBody.name);
        created.GetProperty("sku").GetString().Should().Be(createBody.sku);
        created.GetProperty("unitPrice").GetDecimal().Should().Be(createBody.unitPrice);

        (await client.GetAsync("/api/v1/products?page=1&pageSize=20&search=Urun")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/products/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var mismatch = await client.PutAsJsonAsync($"/api/v1/products/{id}",
            new
            {
                id = Guid.NewGuid(),
                productCategoryId = categoryId,
                name = "X",
                sku = "X1",
                barcode = "1",
                description = "d",
                unit = "Adet",
                unitPrice = 1,
                currency = "TRY"
            });
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(mismatch)).Should().Be("Products.RouteIdMismatch");

        (await client.PostAsync($"/api/v1/products/{id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsync($"/api/v1/products/{id}/activate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Create_Should_Fail_For_DuplicateSku_And_InvalidCategories()
    {
        var client = _factory.CreateClient();
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[]
        {
            "Products.Create",
            "ProductCategories.Create",
            "ProductCategories.Deactivate"
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var cat = await client.PostAsJsonAsync("/api/v1/product-categories", new { name = $"Cat-{Guid.NewGuid():N}"[..13] });
        var catId = (await cat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var sku = $"SKU-{Guid.NewGuid():N}"[..14];
        var body = new
        {
            productCategoryId = catId,
            name = "Urun A",
            sku,
            barcode = "123",
            description = "d",
            unit = "Adet",
            unitPrice = 5,
            currency = "TRY"
        };

        (await client.PostAsJsonAsync("/api/v1/products", body)).StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await client.PostAsJsonAsync("/api/v1/products", body with { name = "Urun B" });
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(duplicate)).Should().Be("Products.SkuAlreadyExists");

        var invalidCategory = await client.PostAsJsonAsync("/api/v1/products", body with
        {
            sku = $"SKU-{Guid.NewGuid():N}"[..14],
            productCategoryId = Guid.NewGuid()
        });
        invalidCategory.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemCodeAsync(invalidCategory)).Should().Be("Products.CategoryNotFound");

        (await client.PostAsync($"/api/v1/product-categories/{catId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var inactiveCategory = await client.PostAsJsonAsync("/api/v1/products", body with
        {
            sku = $"SKU-{Guid.NewGuid():N}"[..14],
            name = "Urun C"
        });
        inactiveCategory.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(inactiveCategory)).Should().Be("Products.CategoryInactive");
    }

    [Fact]
    public async Task Detail_Should_ReturnNotFound_For_OtherTenant_Product()
    {
        var ownerClient = _factory.CreateClient();
        var (_, ownerToken) = await SeedTenantAndIssueTokenAsync(new[] { "Products.Create", "Products.Read" });
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var create = await ownerClient.PostAsJsonAsync("/api/v1/products", new
        {
            productCategoryId = (Guid?)null,
            name = "OwnerProduct",
            sku = $"SKU-{Guid.NewGuid():N}"[..14],
            barcode = "111",
            description = "d",
            unit = "Adet",
            unitPrice = 1,
            currency = "TRY"
        });
        var productId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var otherClient = _factory.CreateClient();
        var (_, otherToken) = await SeedTenantAndIssueTokenAsync(new[] { "Products.Read" });
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var response = await otherClient.GetAsync($"/api/v1/products/{productId}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemCodeAsync(response)).Should().Be("Products.NotFound");
    }

    [Fact]
    public async Task Create_And_Update_Should_Fail_When_UnitPriceNegative()
    {
        var client = _factory.CreateClient();
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[] { "Products.Create", "Products.Update" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var negativeCreate = await client.PostAsJsonAsync("/api/v1/products", new
        {
            productCategoryId = (Guid?)null,
            name = "Neg",
            sku = $"SKU-{Guid.NewGuid():N}"[..14],
            barcode = "1",
            description = "d",
            unit = "Adet",
            unitPrice = -1,
            currency = "TRY"
        });
        negativeCreate.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var create = await client.PostAsJsonAsync("/api/v1/products", new
        {
            productCategoryId = (Guid?)null,
            name = "Pos",
            sku = $"SKU-{Guid.NewGuid():N}"[..14],
            barcode = "2",
            description = "d",
            unit = "Adet",
            unitPrice = 2,
            currency = "TRY"
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var negativeUpdate = await client.PutAsJsonAsync($"/api/v1/products/{id}", new
        {
            id,
            productCategoryId = (Guid?)null,
            name = "Pos",
            sku = $"SKU-{Guid.NewGuid():N}"[..14],
            barcode = "2",
            description = "d",
            unit = "Adet",
            unitPrice = -5,
            currency = "TRY"
        });
        negativeUpdate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Write_Should_Be_Blocked_For_ReadOnlyTenant()
    {
        var client = _factory.CreateClient();
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[] { "Products.Create", "Products.Update" }, readOnlyTenant: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            productCategoryId = (Guid?)null,
            name = "RO",
            sku = $"SKU-{Guid.NewGuid():N}"[..14],
            barcode = "x",
            description = "d",
            unit = "Adet",
            unitPrice = 1,
            currency = "TRY"
        });

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
