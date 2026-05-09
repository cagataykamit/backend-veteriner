using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.ProductStocks;

[Collection("products-api")]
public sealed class ProductStocksEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductStocksEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetList_Should_Return401_When_TokenMissing()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/product-stocks");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetList_Should_Return403_When_MissingProductsRead()
    {
        var client = _factory.CreateClient();
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[] { "Permissions.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/product-stocks");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetList_Should_Return200_When_ProductsRead()
    {
        var client = _factory.CreateClient();
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[] { "Products.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/product-stocks?page=1&pageSize=20");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetByProductId_Should_Return200_With_Stocks_When_ProductsRead()
    {
        var seed = await SeedTenantProductAndStocksAsync(clinicCount: 1, forceBelowMinimum: false);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.Token);

        var response = await client.GetAsync($"/api/v1/products/{seed.ProductId}/stocks");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
        json.GetArrayLength().Should().Be(1);
        json[0].GetProperty("productId").GetGuid().Should().Be(seed.ProductId);
    }

    [Fact]
    public async Task GetByProductId_Should_Return404_When_ProductNotInTenant()
    {
        var client = _factory.CreateClient();
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[] { "Products.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/products/{Guid.NewGuid()}/stocks");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemCodeAsync(response)).Should().Be("Products.NotFound");
    }

    [Fact]
    public async Task CrossTenant_Should_NotExpose_Product_Or_Stocks()
    {
        var a = await SeedTenantProductAndStocksAsync(1, false);
        var b = await SeedTenantProductAndStocksAsync(1, false);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.Token);

        var productProbe = await client.GetAsync($"/api/v1/products/{b.ProductId}");
        productProbe.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var stocksProbe = await client.GetAsync($"/api/v1/products/{b.ProductId}/stocks");
        stocksProbe.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await client.GetAsync("/api/v1/product-stocks?page=1&pageSize=100");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await list.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var row in json.GetProperty("items").EnumerateArray())
        {
            row.GetProperty("productId").GetGuid().Should().NotBe(b.ProductId);
        }
    }

    [Fact]
    public async Task ClinicAdmin_Should_List_AssignedClinicOnly_And_DenyOtherClinicQuery()
    {
        var ctx = await SeedClinicAdminTwoClinicsAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var list = await client.GetAsync("/api/v1/product-stocks?page=1&pageSize=50");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await list.Content.ReadFromJsonAsync<JsonElement>();
        var ids = json.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToArray();
        ids.Should().ContainSingle().Which.Should().Be(ctx.StockA_Id);

        var denied = await client.GetAsync($"/api/v1/product-stocks?clinicId={ctx.ClinicB}&page=1&pageSize=20");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemCodeAsync(denied)).Should().Be("Clinics.AccessDenied");

        var nested = await client.GetAsync($"/api/v1/products/{ctx.ProductId}/stocks");
        nested.StatusCode.Should().Be(HttpStatusCode.OK);
        var arr = await nested.Content.ReadFromJsonAsync<JsonElement>();
        arr.GetArrayLength().Should().Be(1);
        arr[0].GetProperty("clinicId").GetGuid().Should().Be(ctx.ClinicA);
    }

    [Fact]
    public async Task GetList_Should_Filter_IsBelowMinimum_And_Clinic()
    {
        var ctx = await SeedTenantTwoClinicsBelowMinAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var below = await client.GetAsync(
            $"/api/v1/product-stocks?page=1&pageSize=50&clinicId={ctx.ClinicLow}&isBelowMinimum=true");
        below.StatusCode.Should().Be(HttpStatusCode.OK);
        var belowJson = await below.Content.ReadFromJsonAsync<JsonElement>();
        belowJson.GetProperty("items").GetArrayLength().Should().Be(1);
        belowJson.GetProperty("items")[0].GetProperty("isBelowMinimum").GetBoolean().Should().BeTrue();

        var byClinic = await client.GetAsync($"/api/v1/product-stocks?page=1&pageSize=50&clinicId={ctx.ClinicOk}");
        byClinic.StatusCode.Should().Be(HttpStatusCode.OK);
        var okJson = await byClinic.Content.ReadFromJsonAsync<JsonElement>();
        okJson.GetProperty("items").EnumerateArray()
            .Should().ContainSingle()
            .Which.GetProperty("quantityOnHand").GetDecimal().Should().Be(100);
    }

    private async Task<(Guid TenantId, string AccessToken)> SeedTenantAndIssueTokenAsync(
        IReadOnlyCollection<string> permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(
            tenant.Id,
            SubscriptionPlanCode.Basic,
            DateTime.UtcNow,
            14));
        await db.SaveChangesAsync();

        var claims = permissions
            .Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D")))
            .ToList();

        var (accessToken, _, _) = jwt.Create(Guid.NewGuid(), $"it-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return (tenant.Id, accessToken);
    }

    private async Task<TenantStockSeedResult> SeedTenantProductAndStocksAsync(
        int clinicCount,
        bool forceBelowMinimum)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"ps-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(
            tenant.Id,
            SubscriptionPlanCode.Basic,
            DateTime.UtcNow,
            14));

        var product = new Product(
            tenant.Id,
            $"p-{Guid.NewGuid():N}"[..16],
            "Adet",
            1m,
            "TRY",
            sku: $"SKU-{Guid.NewGuid():N}"[..14]);
        db.Products.Add(product);

        var clinics = new List<Clinic>();
        for (var i = 0; i < clinicCount; i++)
        {
            var clinic = new Clinic(tenant.Id, $"Cl-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            clinics.Add(clinic);
        }

        await db.SaveChangesAsync();

        foreach (var clinic in clinics)
        {
            var minimum = forceBelowMinimum ? 100m : 1m;
            db.ProductStocks.Add(new ProductStock(
                tenant.Id,
                clinic.Id,
                product.Id,
                quantityOnHand: 5,
                minimumStockLevel: minimum));
        }

        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim("permission", "Products.Read"),
            new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (token, _, _) = jwt.Create(Guid.NewGuid(), $"ro-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);

        return new TenantStockSeedResult(tenant.Id, product.Id, clinics.First().Id, token);
    }

    private sealed record TenantStockSeedResult(Guid TenantId, Guid ProductId, Guid ClinicId, string Token);

    private async Task<ClinicAdminSeedResult> SeedClinicAdminTwoClinicsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"ca-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(
            tenant.Id,
            SubscriptionPlanCode.Basic,
            DateTime.UtcNow,
            14));

        var user = new User($"ca-{Guid.NewGuid():N}@example.com", hasher.Hash("pw-integration"));
        db.Users.Add(user);
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));

        var clinicAdminClaimId = await db.OperationClaims
            .Where(c => c.Name == "ClinicAdmin")
            .Select(c => c.Id)
            .FirstAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, clinicAdminClaimId));

        var clinicA = new Clinic(tenant.Id, $"CA-{Guid.NewGuid():N}"[..14], "Bursa");
        var clinicB = new Clinic(tenant.Id, $"CB-{Guid.NewGuid():N}"[..14], "Bursa");
        db.Clinics.AddRange(clinicA, clinicB);
        await db.SaveChangesAsync();

        db.UserClinics.Add(new UserClinic(user.Id, clinicA.Id));

        var product = new Product(
            tenant.Id,
            $"mix-{Guid.NewGuid():N}"[..14],
            "Adet",
            2m,
            "TRY",
            sku: $"SKU-{Guid.NewGuid():N}"[..14]);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var stockA = new ProductStock(tenant.Id, clinicA.Id, product.Id, 4m, 2m);
        var stockB = new ProductStock(tenant.Id, clinicB.Id, product.Id, 9m, 3m);
        db.ProductStocks.AddRange(stockA, stockB);
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim("permission", "Products.Read"),
            new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (token, _, _) = jwt.Create(user.Id, user.Email, Array.Empty<string>(), claims);

        return new ClinicAdminSeedResult(
            tenant.Id,
            user.Id,
            clinicA.Id,
            clinicB.Id,
            product.Id,
            stockA.Id,
            stockB.Id,
            token);
    }

    private sealed record ClinicAdminSeedResult(
        Guid TenantId,
        Guid UserId,
        Guid ClinicA,
        Guid ClinicB,
        Guid ProductId,
        Guid StockA_Id,
        Guid StockB_Id,
        string Token);

    private async Task<TwoClinicMinSeedResult> SeedTenantTwoClinicsBelowMinAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"fil-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(
            tenant.Id,
            SubscriptionPlanCode.Basic,
            DateTime.UtcNow,
            14));

        var clinicLow = new Clinic(tenant.Id, $"Low-{Guid.NewGuid():N}"[..12], "Antalya");
        var clinicOk = new Clinic(tenant.Id, $"Ok-{Guid.NewGuid():N}"[..12], "Antalya");
        db.Clinics.AddRange(clinicLow, clinicOk);

        var product = new Product(
            tenant.Id,
            $"filter-{Guid.NewGuid():N}"[..14],
            "Adet",
            1m,
            "TRY",
            sku: $"SKU-{Guid.NewGuid():N}"[..14]);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        db.ProductStocks.Add(new ProductStock(tenant.Id, clinicLow.Id, product.Id, 1m, 50m));
        db.ProductStocks.Add(new ProductStock(tenant.Id, clinicOk.Id, product.Id, 100m, 1m));
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim("permission", "Products.Read"),
            new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (token, _, _) = jwt.Create(Guid.NewGuid(), $"fil-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);

        return new TwoClinicMinSeedResult(clinicLow.Id, clinicOk.Id, token);
    }

    private sealed record TwoClinicMinSeedResult(Guid ClinicLow, Guid ClinicOk, string Token);

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
