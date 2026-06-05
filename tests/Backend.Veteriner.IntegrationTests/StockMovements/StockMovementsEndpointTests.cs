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

namespace Backend.IntegrationTests.StockMovements;

[Collection("products-api")]
public sealed class StockMovementsEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public StockMovementsEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetList_Should_Return401_When_TokenMissing()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/stock-movements");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetList_Should_Return403_When_MissingStockMovementsRead()
    {
        var client = _factory.CreateClient();
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[] { "Products.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/stock-movements?page=1&pageSize=20");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetList_Should_Return200_When_StockMovementsRead()
    {
        var client = _factory.CreateClient();
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[] { "StockMovements.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/stock-movements?page=1&pageSize=20");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetNested_Should_Return200_Paged_When_DataExists()
    {
        var seed = await SeedTenantWithMovementsAsync(movementsPerClinic: 1);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.Token);

        var response = await client.GetAsync(
            $"/api/v1/products/{seed.ProductId}/stock-movements?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").GetArrayLength().Should().Be(1);
        json.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(seed.MovementId);
    }

    [Fact]
    public async Task GetNested_Should_Return404_When_ProductMissing()
    {
        var (_, token) = await SeedTenantAndIssueTokenAsync(new[] { "StockMovements.Read" });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(
            $"/api/v1/products/{Guid.NewGuid()}/stock-movements?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemCodeAsync(response)).Should().Be("Products.NotFound");
    }

    [Fact]
    public async Task CrossTenant_Should_NotExpose_Movements()
    {
        var a = await SeedTenantWithMovementsAsync(1);
        var b = await SeedTenantWithMovementsAsync(1);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.Token);

        var nested = await client.GetAsync(
            $"/api/v1/products/{b.ProductId}/stock-movements?page=1&pageSize=50");

        nested.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await client.GetAsync("/api/v1/stock-movements?page=1&pageSize=200");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await list.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var row in json.GetProperty("items").EnumerateArray())
        {
            row.GetProperty("id").GetGuid().Should().NotBe(b.MovementId);
        }
    }

    [Fact]
    public async Task ClinicAdmin_Should_List_AssignedClinic_Movements_And_DenyOtherClinicFilter()
    {
        var ctx = await SeedClinicAdminTwoClinicsWithMovementsAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var list = await client.GetAsync("/api/v1/stock-movements?page=1&pageSize=50");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var ids = (await list.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid()).ToArray();
        ids.Should().ContainSingle().Which.Should().Be(ctx.MovementA_Id);

        var denied = await client.GetAsync(
            $"/api/v1/stock-movements?clinicId={ctx.ClinicB}&page=1&pageSize=20");

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemCodeAsync(denied)).Should().Be("Clinics.AccessDenied");

        var nested = await client.GetAsync(
            $"/api/v1/products/{ctx.ProductId}/stock-movements?page=1&pageSize=50");

        nested.StatusCode.Should().Be(HttpStatusCode.OK);
        var arr = (await nested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToArray();
        arr.Should().ContainSingle();
        arr[0].GetProperty("clinicId").GetGuid().Should().Be(ctx.ClinicA);
    }

    [Fact]
    public async Task GetList_Should_Filter_MovementType_And_DateRange()
    {
        var ctx = await SeedTenantMovementVarietyAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var onlyIn = await client.GetAsync(
            $"/api/v1/stock-movements?page=1&pageSize=50&productId={ctx.ProductId}&movementType=In");

        onlyIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var inJson = await onlyIn.Content.ReadFromJsonAsync<JsonElement>();
        inJson.GetProperty("totalItems").GetInt32().Should().Be(1);
        inJson.GetProperty("items")[0].GetProperty("movementType").GetInt32().Should().Be((int)StockMovementType.In);

        var midWindow = ctx.MiddleOccurredUtc;
        var fromStr = Uri.EscapeDataString(midWindow.AddMinutes(-30).ToUniversalTime().ToString("o"));
        var toStr = Uri.EscapeDataString(midWindow.AddMinutes(30).ToUniversalTime().ToString("o"));
        var url =
            $"/api/v1/stock-movements?page=1&pageSize=50&productId={ctx.ProductId}" +
            $"&dateFromUtc={fromStr}&dateToUtc={toStr}";

        var windowed = await client.GetAsync(url);
        windowed.StatusCode.Should().Be(HttpStatusCode.OK);
        var wJson = await windowed.Content.ReadFromJsonAsync<JsonElement>();
        wJson.GetProperty("totalItems").GetInt32().Should().Be(1);
        wJson.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(ctx.MiddleMovementId);
    }

    [Fact]
    public async Task Post_Should_Return401_When_TokenMissing()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/stock-movements", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_Should_Return403_When_MissingStockMovementsCreate()
    {
        var ctx = await SeedTenantClinicProductWithPermissionsAsync(new[]
        {
            "StockMovements.Read",
            "Products.Read"
        });

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var response = await client.PostAsJsonAsync("/api/v1/stock-movements", new
        {
            clinicId = ctx.ClinicId,
            productId = ctx.ProductId,
            movementType = (int)StockMovementType.Initial,
            quantity = 2m
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_Initial_Then_GetProductStocks_Should_ReflectQuantity()
    {
        var ctx = await SeedTenantClinicProductWithPermissionsAsync(new[]
        {
            "StockMovements.Create",
            "StockMovements.Read",
            "Products.Read"
        });

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var post = await client.PostAsJsonAsync("/api/v1/stock-movements", new
        {
            clinicId = ctx.ClinicId,
            productId = ctx.ProductId,
            movementType = (int)StockMovementType.Initial,
            quantity = 11m
        });
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await client.GetAsync($"/api/v1/product-stocks?page=1&pageSize=20&productId={ctx.ProductId}");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await list.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("quantityOnHand").GetDecimal().Should().Be(11);
    }

    [Fact]
    public async Task Post_In_And_Out_Should_Update_ProductStock()
    {
        var ctx = await SeedTenantClinicProductWithPermissionsAsync(new[]
        {
            "StockMovements.Create",
            "Products.Read"
        });

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        (await client.PostAsJsonAsync("/api/v1/stock-movements", new
        {
            clinicId = ctx.ClinicId,
            productId = ctx.ProductId,
            movementType = (int)StockMovementType.Initial,
            quantity = 10m
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.PostAsJsonAsync("/api/v1/stock-movements", new
        {
            clinicId = ctx.ClinicId,
            productId = ctx.ProductId,
            movementType = (int)StockMovementType.In,
            quantity = 5m
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.PostAsJsonAsync("/api/v1/stock-movements", new
        {
            clinicId = ctx.ClinicId,
            productId = ctx.ProductId,
            movementType = (int)StockMovementType.Out,
            quantity = 7m
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var verify = await client.GetAsync($"/api/v1/product-stocks?page=1&pageSize=10&productId={ctx.ProductId}");
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var j = await verify.Content.ReadFromJsonAsync<JsonElement>();
        j.GetProperty("items")[0].GetProperty("quantityOnHand").GetDecimal().Should().Be(8);
    }

    [Fact]
    public async Task Post_Out_Should_ReturnProblem_When_InsufficientStock()
    {
        var ctx = await SeedTenantClinicProductWithPermissionsAsync(new[]
        {
            "StockMovements.Create",
            "Products.Read"
        });

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        (await client.PostAsJsonAsync("/api/v1/stock-movements", new
        {
            clinicId = ctx.ClinicId,
            productId = ctx.ProductId,
            movementType = (int)StockMovementType.Initial,
            quantity = 2m
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        var bad = await client.PostAsJsonAsync("/api/v1/stock-movements", new
        {
            clinicId = ctx.ClinicId,
            productId = ctx.ProductId,
            movementType = (int)StockMovementType.Out,
            quantity = 50m
        });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(bad)).Should().Be("StockMovements.InsufficientStock");
    }

    [Fact]
    public async Task Post_Should_BeBlocked_When_TenantReadOnly()
    {
        var (_, token) = await SeedTenantAndIssueTokenAsync(
            new[] { "StockMovements.Create", "Products.Read" },
            readOnlyTenant: true);

        var ctx = await SeedTenantClinicProductWithExistingTenantTokenAsync(token);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var response = await client.PostAsJsonAsync("/api/v1/stock-movements", new
        {
            clinicId = ctx.ClinicId,
            productId = ctx.ProductId,
            movementType = (int)StockMovementType.Initial,
            quantity = 1m
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemCodeAsync(response)).Should().Be("Subscriptions.TenantReadOnly");
    }

    [Fact]
    public async Task Post_Should_Return403_When_ClinicAdmin_UnassignedClinic()
    {
        var ctx = await SeedClinicAdminTwoClinicsProductForMutationAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var response = await client.PostAsJsonAsync("/api/v1/stock-movements", new
        {
            clinicId = ctx.ClinicB,
            productId = ctx.ProductId,
            movementType = (int)StockMovementType.Initial,
            quantity = 1m
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemCodeAsync(response)).Should().Be("Clinics.AccessDenied");
    }

    private async Task<MutationSeedCtx> SeedTenantClinicProductWithPermissionsAsync(string[] permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"mut-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));

        var clinic = new Clinic(tenant.Id, $"Mc-{Guid.NewGuid():N}"[..14], "Y");
        db.Clinics.Add(clinic);

        var product = new Product(
            tenant.Id,
            $"mp-{Guid.NewGuid():N}"[..14],
            "Adet",
            2m,
            "TRY",
            sku: $"MSK-{Guid.NewGuid():N}"[..14]);
        db.Products.Add(product);

        await db.SaveChangesAsync();

        var claims = permissions
            .Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D")))
            .ToList();

        var (token, _, _) = jwt.Create(Guid.NewGuid(), $"mut-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);

        return new MutationSeedCtx(clinic.Id, product.Id, token);
    }

    private async Task<MutationSeedCtx> SeedTenantClinicProductWithExistingTenantTokenAsync(string token)
    {
        var tenantId = ExtractTenantIdFromJwt(token);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var clinic = new Clinic(tenantId, $"Ro-{Guid.NewGuid():N}"[..14], "Z");
        db.Clinics.Add(clinic);
        var product = new Product(
            tenantId,
            $"rp-{Guid.NewGuid():N}"[..14],
            "Adet",
            1m,
            "TRY",
            sku: $"RSK-{Guid.NewGuid():N}"[..14]);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return new MutationSeedCtx(clinic.Id, product.Id, token);
    }

    private static Guid ExtractTenantIdFromJwt(string token)
    {
        var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = tokenHandler.ReadJwtToken(token);
        var tid = jwt.Claims.First(c => c.Type == VeterinerClaims.TenantId).Value;
        return Guid.Parse(tid);
    }

    private async Task<ClinicAdminMutationSeedCtx> SeedClinicAdminTwoClinicsProductForMutationAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"cam-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));

        var user = new User($"cam-{Guid.NewGuid():N}@example.com", hasher.Hash("pw-cam"));
        db.Users.Add(user);
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));

        var clinicAdminClaimId = await db.OperationClaims.Where(c => c.Name == "ClinicAdmin").Select(c => c.Id).FirstAsync();
        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, clinicAdminClaimId));

        var clinicA = new Clinic(tenant.Id, $"MA-{Guid.NewGuid():N}"[..12], "City");
        var clinicB = new Clinic(tenant.Id, $"MB-{Guid.NewGuid():N}"[..12], "City");
        db.Clinics.AddRange(clinicA, clinicB);
        await db.SaveChangesAsync();

        db.UserClinics.Add(new UserClinic(user.Id, clinicA.Id));

        var product = new Product(
            tenant.Id,
            $"cap-{Guid.NewGuid():N}"[..12],
            "Adet",
            1m,
            "TRY",
            sku: $"CAS-{Guid.NewGuid():N}"[..12]);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim("permission", "StockMovements.Create"),
            new Claim("permission", "StockMovements.Read"),
            new Claim("permission", "Products.Read"),
            new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (token, _, _) = jwt.Create(user.Id, user.Email, Array.Empty<string>(), claims);

        return new ClinicAdminMutationSeedCtx(clinicA.Id, clinicB.Id, product.Id, token);
    }

    private sealed record MutationSeedCtx(Guid ClinicId, Guid ProductId, string Token);

    private sealed record ClinicAdminMutationSeedCtx(Guid ClinicA, Guid ClinicB, Guid ProductId, string Token);

    private async Task<(Guid TenantId, string AccessToken)> SeedTenantAndIssueTokenAsync(
        IReadOnlyCollection<string> permissions,
        bool readOnlyTenant = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"sm-{Guid.NewGuid():N}"[..18]);
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

        var (accessToken, _, _) = jwt.Create(Guid.NewGuid(), $"sm-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return (tenant.Id, accessToken);
    }

    private async Task<MovementSeedResult> SeedTenantWithMovementsAsync(int movementsPerClinic)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"mv-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(
            tenant.Id,
            SubscriptionPlanCode.Basic,
            DateTime.UtcNow,
            14));

        var clinic = new Clinic(tenant.Id, $"Cm-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);

        var product = new Product(
            tenant.Id,
            $"prd-{Guid.NewGuid():N}"[..14],
            "Adet",
            5m,
            "TRY",
            sku: $"SKU-{Guid.NewGuid():N}"[..14]);
        db.Products.Add(product);

        await db.SaveChangesAsync();

        StockMovement? first = null;
        for (var i = 0; i < movementsPerClinic; i++)
        {
            var mv = new StockMovement(
                tenant.Id,
                clinic.Id,
                product.Id,
                StockMovementType.In,
                quantity: 2m + i,
                occurredAtUtc: DateTime.UtcNow.AddMinutes(-i),
                notes: $"mv-{Guid.NewGuid():N}");
            db.StockMovements.Add(mv);
            first ??= mv;
        }

        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim("permission", "StockMovements.Read"),
            new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (token, _, _) = jwt.Create(Guid.NewGuid(), $"mv-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);

        return new MovementSeedResult(tenant.Id, product.Id, clinic.Id, first!.Id, token);
    }

    private sealed record MovementSeedResult(
        Guid TenantId,
        Guid ProductId,
        Guid ClinicId,
        Guid MovementId,
        string Token);

    private async Task<ClinicAdminMovementSeedResult> SeedClinicAdminTwoClinicsWithMovementsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"ca-sm-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(
            tenant.Id,
            SubscriptionPlanCode.Basic,
            DateTime.UtcNow,
            14));

        var user = new User($"ca-sm-{Guid.NewGuid():N}@example.com", hasher.Hash("pw-sm"));
        db.Users.Add(user);
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));

        var clinicAdminClaimId = await db.OperationClaims.Where(c => c.Name == "ClinicAdmin").Select(c => c.Id).FirstAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, clinicAdminClaimId));

        var clinicA = new Clinic(tenant.Id, $"SA-{Guid.NewGuid():N}"[..14], "Konya");
        var clinicB = new Clinic(tenant.Id, $"SB-{Guid.NewGuid():N}"[..14], "Konya");
        db.Clinics.AddRange(clinicA, clinicB);
        await db.SaveChangesAsync();

        db.UserClinics.Add(new UserClinic(user.Id, clinicA.Id));

        var product = new Product(
            tenant.Id,
            $"pmix-{Guid.NewGuid():N}"[..14],
            "Adet",
            3m,
            "TRY",
            sku: $"SKU-{Guid.NewGuid():N}"[..14]);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var mvA = new StockMovement(
            tenant.Id,
            clinicA.Id,
            product.Id,
            StockMovementType.In,
            2m,
            DateTime.UtcNow.AddHours(-1));

        var mvB = new StockMovement(
            tenant.Id,
            clinicB.Id,
            product.Id,
            StockMovementType.Out,
            1m,
            DateTime.UtcNow.AddHours(-2));

        db.StockMovements.AddRange(mvA, mvB);
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim("permission", "StockMovements.Read"),
            new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (token, _, _) = jwt.Create(user.Id, user.Email, Array.Empty<string>(), claims);

        return new ClinicAdminMovementSeedResult(
            clinicA.Id,
            clinicB.Id,
            product.Id,
            mvA.Id,
            mvB.Id,
            token);
    }

    private sealed record ClinicAdminMovementSeedResult(
        Guid ClinicA,
        Guid ClinicB,
        Guid ProductId,
        Guid MovementA_Id,
        Guid MovementB_Id,
        string Token);

    private async Task<DateFilterSeedResult> SeedTenantMovementVarietyAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"dv-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(
            tenant.Id,
            SubscriptionPlanCode.Basic,
            DateTime.UtcNow,
            14));

        var clinic = new Clinic(tenant.Id, $"Cd-{Guid.NewGuid():N}"[..14], "Trabzon");
        db.Clinics.Add(clinic);

        var product = new Product(
            tenant.Id,
            $"pvar-{Guid.NewGuid():N}"[..14],
            "Adet",
            8m,
            "TRY",
            sku: $"SKU-{Guid.NewGuid():N}"[..14]);
        db.Products.Add(product);

        await db.SaveChangesAsync();

        var early = new StockMovement(
            tenant.Id,
            clinic.Id,
            product.Id,
            StockMovementType.In,
            1m,
            new DateTime(2030, 6, 1, 10, 0, 0, DateTimeKind.Utc));

        var mvMiddle = new StockMovement(
            tenant.Id,
            clinic.Id,
            product.Id,
            StockMovementType.Out,
            2m,
            new DateTime(2030, 6, 15, 12, 0, 0, DateTimeKind.Utc));

        var late = new StockMovement(
            tenant.Id,
            clinic.Id,
            product.Id,
            StockMovementType.Adjustment,
            1m,
            new DateTime(2030, 7, 1, 8, 0, 0, DateTimeKind.Utc));

        db.StockMovements.AddRange(early, mvMiddle, late);
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim("permission", "StockMovements.Read"),
            new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (token, _, _) = jwt.Create(Guid.NewGuid(), $"dv-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);

        return new DateFilterSeedResult(product.Id, mvMiddle.Id, mvMiddle.OccurredAtUtc, token);
    }

    private sealed record DateFilterSeedResult(Guid ProductId, Guid MiddleMovementId, DateTime MiddleOccurredUtc, string Token);

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
