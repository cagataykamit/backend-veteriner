using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.GoodsReceipts;

/// <summary>
/// Mal kabul (gerçek SQL Server/LocalDB): çok satırlı giriş atomiktir, satır kuralları mevcut giriş komutundan gelir,
/// requestId ile tekrar çift giriş üretmez, eşzamanlı aynı istek tutarlı kalır.
/// </summary>
[Collection("products-api")]
public sealed class GoodsReceiptsEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] Permissions = { "StockMovements.Create", "StockMovements.Read" };

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static string Date(int daysFromToday) => Today.AddDays(daysFromToday).ToString("yyyy-MM-dd");

    private readonly CustomWebApplicationFactory _factory;

    public GoodsReceiptsEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Receipt_Should_Write_All_Lines_As_Separate_Batches_With_Shared_Invoice_Fields()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);

        var response = await PostAsync(client, ctx, Guid.NewGuid(), new object[]
        {
            Line(ctx.Plain, 10m, unitCost: 4.5m),
            Line(ctx.Vaccine, 5m, "AS-1", Date(120)),
            Line(ctx.Plain, 3m, "P-2", Date(60)),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("created").GetBoolean().Should().BeTrue();
        var lines = json.GetProperty("lines").EnumerateArray().ToList();
        lines.Select(l => l.GetProperty("productId").GetGuid()).Should().Equal(ctx.Plain, ctx.Vaccine, ctx.Plain);
        lines[0].GetProperty("unitCost").GetDecimal().Should().Be(4.5m);
        lines[0].GetProperty("reason").GetString().Should().Be("Mal kabul");

        // Lotsuz satır da başlık bilgisi taşıdığı için parti açar (fatura/firma defteri).
        var batchId = lines[1].GetProperty("batchId").GetGuid();
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/stock-batches/{batchId}");
        detail.GetProperty("lotNumber").GetString().Should().Be("AS-1");
        detail.GetProperty("supplierName").GetString().Should().Be("Firma A");
        detail.GetProperty("invoiceNumber").GetString().Should().Be("FTR-77");
        detail.GetProperty("receivedDate").GetString().Should().Be(Date(-1));
        detail.GetProperty("coldChainMonitor").GetString().Should().Be("Logger-1");

        (await StockAsync(ctx.Plain)).Should().Be(13m);
        (await StockAsync(ctx.Vaccine)).Should().Be(5m);
        (await BatchCountAsync(ctx)).Should().Be(3);
        (await MovementCountAsync(ctx)).Should().Be(3);
    }

    [Fact]
    public async Task Failing_Line_Should_Write_Nothing_And_Name_The_Line()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);

        var response = await PostAsync(client, ctx, Guid.NewGuid(), new object[]
        {
            Line(ctx.Plain, 10m),
            Line(ctx.Plain, 2m),
            Line(ctx.Vaccine, 5m), // aşı: lot + SKT zorunlu
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("StockBatches.LotAndExpiryRequired");
        problem.GetProperty("detail").GetString().Should().StartWith("Satır 3:");
        (await StockAsync(ctx.Plain)).Should().Be(0m);
        (await StockAsync(ctx.Vaccine)).Should().Be(0m);
        (await BatchCountAsync(ctx)).Should().Be(0);
        (await MovementCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task Expired_Line_Should_Need_Its_Own_Acknowledgement()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var requestId = Guid.NewGuid();

        var rejected = await PostAsync(client, ctx, requestId, new object[]
        {
            Line(ctx.Plain, 1m, "OK", Date(30)),
            Line(ctx.Plain, 2m, "OLD", Date(-5)),
        });

        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("StockBatches.ExpiredAcknowledgementRequired");
        problem.GetProperty("detail").GetString().Should().StartWith("Satır 2:");
        (await MovementCountAsync(ctx)).Should().Be(0);

        // Hata sonrası aynı requestId ile düzeltilmiş gövde gönderilebilir.
        var accepted = await PostAsync(client, ctx, requestId, new object[]
        {
            Line(ctx.Plain, 1m, "OK", Date(30)),
            Line(ctx.Plain, 2m, "OLD", Date(-5), acknowledgeExpired: true),
        });

        accepted.StatusCode.Should().Be(HttpStatusCode.Created);
        (await StockAsync(ctx.Plain)).Should().Be(3m);
        (await BatchCountAsync(ctx)).Should().Be(2);
    }

    [Fact]
    public async Task Same_RequestId_Should_Replay_Without_Double_Entry_And_Reject_Different_Content()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var requestId = Guid.NewGuid();
        var body = new object[] { Line(ctx.Plain, 10m, "L1", Date(90)), Line(ctx.Vaccine, 4m, "L2", Date(90)) };

        var first = await PostAsync(client, ctx, requestId, body);
        var second = await PostAsync(client, ctx, requestId, body);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondJson = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondJson.GetProperty("created").GetBoolean().Should().BeFalse();
        var firstIds = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lines").EnumerateArray()
            .Select(l => l.GetProperty("id").GetGuid());
        secondJson.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).Should().Equal(firstIds);
        (await BatchCountAsync(ctx)).Should().Be(2);
        (await StockAsync(ctx.Plain)).Should().Be(10m);

        var moreLines = await PostAsync(client, ctx, requestId, body.Append(Line(ctx.Plain, 1m, "L3", Date(90))).ToArray());
        var otherQuantity = await PostAsync(client, ctx, requestId, new object[] { Line(ctx.Plain, 11m, "L1", Date(90)), Line(ctx.Vaccine, 4m, "L2", Date(90)) });

        moreLines.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadCodeAsync(moreLines)).Should().Be("GoodsReceipts.DuplicateRequestId");
        otherQuantity.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadCodeAsync(otherQuantity)).Should().Be("GoodsReceipts.DuplicateRequestId");
        (await BatchCountAsync(ctx)).Should().Be(2);
        (await StockAsync(ctx.Plain)).Should().Be(10m);
        (await MovementCountAsync(ctx)).Should().Be(2);
    }

    [Fact]
    public async Task Parallel_Identical_Requests_Should_Enter_Stock_Once()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var requestId = Guid.NewGuid();
        var body = new object[] { Line(ctx.Plain, 6m, "C1", Date(90)), Line(ctx.Vaccine, 2m, "C2", Date(90)) };

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostAsync(client, ctx, requestId, body)));

        responses.Select(r => r.StatusCode).Should().Contain(HttpStatusCode.Created);
        responses.Select(r => r.StatusCode).Should().OnlyContain(s =>
            s == HttpStatusCode.Created || s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
        (await StockAsync(ctx.Plain)).Should().Be(6m);
        (await StockAsync(ctx.Vaccine)).Should().Be(2m);
        (await BatchCountAsync(ctx)).Should().Be(2);
        (await MovementCountAsync(ctx)).Should().Be(2);
    }

    [Fact]
    public async Task Structural_Errors_Should_Return400_With_Line_Path_And_Write_Nothing()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);

        var badQuantity = await PostAsync(client, ctx, Guid.NewGuid(), new object[] { Line(ctx.Plain, 1m), Line(ctx.Plain, 0m) });
        badQuantity.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = (await badQuantity.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        errors.TryGetProperty("lines[1].quantity", out _).Should().BeTrue();

        var tooMany = await PostAsync(
            client, ctx, Guid.NewGuid(), Enumerable.Range(0, 101).Select(_ => (object)Line(ctx.Plain, 1m)).ToArray());
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var noLines = await PostAsync(client, ctx, Guid.NewGuid(), Array.Empty<object>());
        noLines.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var noRequestId = await PostAsync(client, ctx, Guid.Empty, new object[] { Line(ctx.Plain, 1m) });
        noRequestId.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var unknownProduct = await client.PostAsJsonAsync("/api/v1/goods-receipts", new
        {
            clinicId = ctx.ClinicId,
            requestId = Guid.NewGuid(),
            lines = new object[] { Line(ctx.Plain, 1m), new { productId = Guid.NewGuid(), quantity = 1m } },
        });
        unknownProduct.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadCodeAsync(unknownProduct)).Should().Be("Products.NotFound");

        (await MovementCountAsync(ctx)).Should().Be(0);
        (await StockAsync(ctx.Plain)).Should().Be(0m);
    }

    [Fact]
    public async Task Receipt_Should_Require_StockMovements_Create_Permission()
    {
        var ctx = await SeedAsync();
        var readOnly = await SeedClientAsync(ctx, new[] { "StockMovements.Read" });

        var response = await PostAsync(readOnly, ctx, Guid.NewGuid(), new object[] { Line(ctx.Plain, 1m) });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await MovementCountAsync(ctx)).Should().Be(0);
    }

    // ---------------------------------------------------------------- Yardımcılar

    private static object Line(
        Guid productId, decimal quantity, string? lot = null, string? expiry = null, decimal? unitCost = null, bool? acknowledgeExpired = null)
        => new { productId, quantity, lotNumber = lot, expiryDate = expiry, unitCost, acknowledgeExpired };

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, SeedCtx ctx, Guid requestId, object[] lines)
        => client.PostAsJsonAsync("/api/v1/goods-receipts", new
        {
            clinicId = ctx.ClinicId,
            requestId,
            supplierName = "Firma A",
            invoiceNumber = "FTR-77",
            invoiceDate = Date(-3),
            receivedDate = Date(-1),
            transportMethod = "Soğuk zincir",
            coldChainMonitor = "Logger-1",
            lines,
        });

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private HttpClient ClientFor(SeedCtx ctx)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        return client;
    }

    private async Task<HttpClient> SeedClientAsync(SeedCtx ctx, string[] permissions)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, ctx.TenantId, ctx.ClinicId, permissions, includeClinicClaimInJwt: true);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<decimal> StockAsync(Guid productId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProductStocks.AsNoTracking()
            .Where(s => s.ProductId == productId).Select(s => s.QuantityOnHand).SingleOrDefaultAsync();
    }

    private async Task<int> BatchCountAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.StockBatches.AsNoTracking().CountAsync(b => b.TenantId == ctx.TenantId);
    }

    private async Task<int> MovementCountAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.StockMovements.AsNoTracking().CountAsync(m => m.TenantId == ctx.TenantId);
    }

    private async Task<SeedCtx> SeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"gr-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Gr-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var plain = new Product(
            tenant.Id, $"gp-{Guid.NewGuid():N}"[..14], "Doz", 2m, "TRY", sku: $"GSK-{Guid.NewGuid():N}"[..14]);
        var vaccine = new Product(
            tenant.Id, $"gv-{Guid.NewGuid():N}"[..14], "Doz", 2m, "TRY", sku: $"GSV-{Guid.NewGuid():N}"[..14],
            kind: ProductKind.Vaccine);
        db.Products.AddRange(plain, vaccine);
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, Permissions, includeClinicClaimInJwt: true);

        return new SeedCtx(tenant.Id, clinic.Id, plain.Id, vaccine.Id, token);
    }

    private sealed record SeedCtx(Guid TenantId, Guid ClinicId, Guid Plain, Guid Vaccine, string Token);
}
