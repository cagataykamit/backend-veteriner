using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ardalis.Specification;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.StockMovements.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Repositories;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.StockBatches;

/// <summary>
/// ADR-015 (gerçek SQL Server/LocalDB): giriş parti açar, çıkış/uygulama FEFO veya seçilen partiden düşer, eski (partisiz) bakiye bozulmaz,
/// SKT geçmiş partide uygulama engellenir, ters hareket/iptal aynı partiye döner, eşzamanlı düşüm ve requestId yarışı tutarlı kalır.
/// </summary>
[Collection("products-api")]
public sealed class StockBatchesEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AllPermissions =
    {
        "StockMovements.Create", "StockMovements.Read", "StockMovements.Correct",
        "Administrations.Create", "Administrations.Read", "Administrations.Correct",
        "Products.Read", "Products.Update",
    };

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static string Date(int daysFromToday) => Today.AddDays(daysFromToday).ToString("yyyy-MM-dd");

    private readonly CustomWebApplicationFactory _factory;

    public StockBatchesEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ---------------------------------------------------------------- Giriş / okuma

    [Fact]
    public async Task In_With_Batch_Should_Create_Batch_And_Expose_It_In_List_Detail_And_Product_Balance()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);

        var response = await PostMovementAsync(client, ctx, 1, 10m, batch: new
        {
            lotNumber = "LOT-1",
            expiryDate = Date(200),
            supplierName = "Firma A",
            invoiceNumber = "FTR-1",
            invoiceDate = Date(-3),
            receivedDate = Date(-2),
            dispatchedDate = Date(-4),
            transportMethod = "Soğuk zincir",
            coldChainMonitor = "Logger-7",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var movement = await response.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = movement.GetProperty("batchId").GetGuid();
        movement.GetProperty("batchBalanceAfter").GetDecimal().Should().Be(10m);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/stock-batches/{batchId}");
        detail.GetProperty("lotNumber").GetString().Should().Be("LOT-1");
        detail.GetProperty("expiryDate").GetString().Should().Be(Date(200));
        detail.GetProperty("supplierName").GetString().Should().Be("Firma A");
        detail.GetProperty("invoiceNumber").GetString().Should().Be("FTR-1");
        detail.GetProperty("receivedDate").GetString().Should().Be(Date(-2));
        detail.GetProperty("dispatchedDate").GetString().Should().Be(Date(-4));
        detail.GetProperty("coldChainMonitor").GetString().Should().Be("Logger-7");
        detail.GetProperty("initialQuantity").GetDecimal().Should().Be(10m);
        detail.GetProperty("remainingQuantity").GetDecimal().Should().Be(10m);
        detail.GetProperty("isExpired").GetBoolean().Should().BeFalse();
        detail.GetProperty("unit").GetString().Should().Be("Doz");

        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/stock-batches?clinicId={ctx.ClinicId}&productId={ctx.ProductId}");
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(batchId);

        var balance = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/products/{ctx.ProductId}/stock-batches?clinicId={ctx.ClinicId}");
        balance.GetProperty("quantityOnHand").GetDecimal().Should().Be(10m);
        balance.GetProperty("batchedQuantity").GetDecimal().Should().Be(10m);
        balance.GetProperty("unbatchedQuantity").GetDecimal().Should().Be(0m);
        balance.GetProperty("batches").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task In_For_Vaccine_Without_Lot_Or_Expiry_Should_Return400_And_Not_Change_Stock()
    {
        var ctx = await SeedAsync(ProductKind.Vaccine);
        var client = ClientFor(ctx);

        var noBatch = await PostMovementAsync(client, ctx, 1, 5m);
        var noExpiry = await PostMovementAsync(client, ctx, 1, 5m, batch: new { lotNumber = "V1" });

        noBatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(noBatch)).Should().Be("StockBatches.LotAndExpiryRequired");
        noExpiry.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetStockAsync(ctx)).Should().Be(0m);
        (await BatchCountAsync(ctx)).Should().Be(0);

        var ok = await PostMovementAsync(client, ctx, 1, 5m, batch: new { lotNumber = "V1", expiryDate = Date(90) });
        ok.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task In_For_Controlled_Product_Should_Require_Lot_And_Expiry()
    {
        var ctx = await SeedAsync(ProductKind.Medicine, ControlledSubstanceClass.Narcotic);

        var response = await PostMovementAsync(ClientFor(ctx), ctx, 1, 2m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("StockBatches.LotAndExpiryRequired");
    }

    [Fact]
    public async Task In_Without_Batch_For_Plain_Product_Should_Be_Unbatched_And_Keep_Old_Contract()
    {
        var ctx = await SeedAsync(ProductKind.Other);
        var client = ClientFor(ctx);

        var response = await PostMovementAsync(client, ctx, 1, 4m);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("batchId").ValueKind.Should().Be(JsonValueKind.Null);
        (await BatchCountAsync(ctx)).Should().Be(0);
        var balance = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/products/{ctx.ProductId}/stock-batches?clinicId={ctx.ClinicId}");
        balance.GetProperty("unbatchedQuantity").GetDecimal().Should().Be(4m);
        balance.GetProperty("batches").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task In_Should_Reject_BatchId_And_Out_Should_Reject_Batch_Details()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);

        var inWithBatchId = await client.PostAsJsonAsync("/api/v1/stock-movements", new
        {
            clinicId = ctx.ClinicId, productId = ctx.ProductId, movementType = 1, quantity = 1, batchId = Guid.NewGuid(),
        });
        var outWithDetails = await PostMovementAsync(client, ctx, 2, 1m, batch: new { lotNumber = "X" });

        inWithBatchId.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        outWithDetails.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_Should_Filter_Expired_And_Available_And_Order_By_Fefo()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var late = await EnterBatchAsync(client, ctx, 5m, "LATE", Date(300));
        var soon = await EnterBatchAsync(client, ctx, 5m, "SOON", Date(30));
        var expired = await EnterBatchAsync(client, ctx, 5m, "OLD", Date(-10));
        var noExpiry = await EnterBatchAsync(client, ctx, 5m, "NOEXP", null);
        var drained = await EnterBatchAsync(client, ctx, 2m, "EMPTY", Date(50));
        (await PostMovementAsync(client, ctx, 2, 2m, batchId: drained)).StatusCode.Should().Be(HttpStatusCode.Created);

        var all = await ListIdsAsync(client, ctx, "");
        var onlyExpired = await ListIdsAsync(client, ctx, "&expired=true");
        var notExpired = await ListIdsAsync(client, ctx, "&expired=false");
        var withEmpty = await ListIdsAsync(client, ctx, "&availableOnly=false");
        var soonOnes = await ListIdsAsync(client, ctx, $"&expiresBeforeDate={Date(60)}");
        var byLot = await ListIdsAsync(client, ctx, "&lotNumber=SOO");

        all.Should().Equal(expired, soon, late, noExpiry); // FEFO: SKT artan, SKT'siz sonda, boş parti gizli
        onlyExpired.Should().Equal(expired);
        notExpired.Should().Equal(soon, late, noExpiry);
        withEmpty.Should().Contain(drained).And.HaveCount(5);
        soonOnes.Should().Equal(expired, soon);
        byLot.Should().Equal(soon);
    }

    // ---------------------------------------------------------------- Çıkış / FEFO / eski bakiye

    [Fact]
    public async Task Out_Without_Batch_Should_Use_Fefo_And_Explicit_Batch_Should_Override()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var late = await EnterBatchAsync(client, ctx, 10m, "LATE", Date(300));
        var soon = await EnterBatchAsync(client, ctx, 10m, "SOON", Date(30));

        var fefo = await PostMovementAsync(client, ctx, 2, 3m);
        var chosen = await PostMovementAsync(client, ctx, 2, 4m, batchId: late);

        fefo.StatusCode.Should().Be(HttpStatusCode.Created);
        (await fefo.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("batchId").GetGuid().Should().Be(soon);
        chosen.StatusCode.Should().Be(HttpStatusCode.Created);
        (await RemainingAsync(soon)).Should().Be(7m);
        (await RemainingAsync(late)).Should().Be(6m);
        (await GetStockAsync(ctx)).Should().Be(13m);
    }

    [Fact]
    public async Task Legacy_Stock_Without_Batches_Should_Keep_Working_And_Show_As_Unbatched()
    {
        var ctx = await SeedAsync(legacyStock: 12m); // migration sonrası eski durum: stok var, parti yok
        var client = ClientFor(ctx);

        var legacyOut = await PostMovementAsync(client, ctx, 2, 5m);
        legacyOut.StatusCode.Should().Be(HttpStatusCode.Created);
        (await legacyOut.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("batchId").ValueKind.Should().Be(JsonValueKind.Null);

        var batchId = await EnterBatchAsync(client, ctx, 5m, "NEW", Date(100));
        var balance = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/products/{ctx.ProductId}/stock-batches?clinicId={ctx.ClinicId}");
        balance.GetProperty("quantityOnHand").GetDecimal().Should().Be(12m);
        balance.GetProperty("batchedQuantity").GetDecimal().Should().Be(5m);
        balance.GetProperty("unbatchedQuantity").GetDecimal().Should().Be(7m);

        // 6 > parti(5): tek parti yetmez, partisiz bakiye (7) karşılar.
        var fallback = await PostMovementAsync(client, ctx, 2, 6m);
        fallback.StatusCode.Should().Be(HttpStatusCode.Created);
        (await fallback.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("batchId").ValueKind.Should().Be(JsonValueKind.Null);
        (await RemainingAsync(batchId)).Should().Be(5m);
        (await GetStockAsync(ctx)).Should().Be(6m);

        // Toplam yeterli (6) ama tek kaynak yok (parti 5, partisiz 1): reddedilir, hiçbir şey değişmez.
        var tooBig = await PostMovementAsync(client, ctx, 2, 6m);
        tooBig.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(tooBig)).Should().Be("StockMovements.InsufficientStock");
        (await RemainingAsync(batchId)).Should().Be(5m);
        (await GetStockAsync(ctx)).Should().Be(6m);
    }

    [Fact]
    public async Task Out_With_Too_Small_Batch_Should_Return400_Even_When_Total_Stock_Is_Enough()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var small = await EnterBatchAsync(client, ctx, 2m, "SMALL", Date(100));
        await EnterBatchAsync(client, ctx, 20m, "BIG", Date(200));

        var response = await PostMovementAsync(client, ctx, 2, 3m, batchId: small);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("StockMovements.InsufficientStock");
        (await RemainingAsync(small)).Should().Be(2m);
        (await GetStockAsync(ctx)).Should().Be(22m);
    }

    [Fact]
    public async Task Manual_Out_Should_Be_Allowed_From_Expired_Batch_For_Disposal()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var expired = await EnterBatchAsync(client, ctx, 4m, "OLD", Date(-5));

        var disposal = await PostMovementAsync(client, ctx, 2, 4m, batchId: expired);

        disposal.StatusCode.Should().Be(HttpStatusCode.Created);
        (await RemainingAsync(expired)).Should().Be(0m);
    }

    [Fact]
    public async Task Out_Should_Return404_For_Batch_Of_Another_Product()
    {
        var ctx = await SeedAsync();
        var other = await SeedAsync();
        var client = ClientFor(ctx);
        await PostMovementAsync(client, ctx, 1, 5m);
        var foreign = await EnterBatchAsync(ClientFor(other), other, 5m, "FOREIGN", Date(100));

        var response = await PostMovementAsync(client, ctx, 2, 1m, batchId: foreign);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemCodeAsync(response)).Should().Be("StockBatches.NotFound");
    }

    // ---------------------------------------------------------------- Sayım

    [Fact]
    public async Task Adjustment_With_Batch_Should_Set_Batch_Remaining_And_Without_Batch_Should_Require_One_When_Unbatched_Is_Short()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var batch = await EnterBatchAsync(client, ctx, 10m, "CNT", Date(100));

        var byBatch = await PostMovementAsync(client, ctx, 3, 7m, batchId: batch);
        byBatch.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await byBatch.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("quantity").GetDecimal().Should().Be(3m);
        json.GetProperty("batchBalanceAfter").GetDecimal().Should().Be(7m);
        (await RemainingAsync(batch)).Should().Be(7m);
        (await GetStockAsync(ctx)).Should().Be(7m);

        // Partisiz bakiye 0: toplamı düşüren sayım parti seçmeden yapılamaz.
        var withoutBatch = await PostMovementAsync(client, ctx, 3, 5m);
        withoutBatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(withoutBatch)).Should().Be("StockMovements.BatchRequired");
        (await GetStockAsync(ctx)).Should().Be(7m);

        // Artış partisiz bakiyeye gider.
        var up = await PostMovementAsync(client, ctx, 3, 9m);
        up.StatusCode.Should().Be(HttpStatusCode.Created);
        (await RemainingAsync(batch)).Should().Be(7m);
        (await GetStockAsync(ctx)).Should().Be(9m);
    }

    // ---------------------------------------------------------------- Ters hareket

    [Fact]
    public async Task Reverse_Out_Should_Return_Quantity_To_Same_Batch_And_Reverse_In_Should_Respect_Batch_Remaining()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var entry = await PostMovementAsync(client, ctx, 1, 10m, batch: new { lotNumber = "R1", expiryDate = Date(100) });
        var entryJson = await entry.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = entryJson.GetProperty("batchId").GetGuid();
        var entryMovementId = entryJson.GetProperty("id").GetGuid();
        var outJson = await (await PostMovementAsync(client, ctx, 2, 4m, batchId: batchId)).Content.ReadFromJsonAsync<JsonElement>();

        var reverseOut = await ReverseAsync(client, outJson.GetProperty("id").GetGuid());

        reverseOut.StatusCode.Should().Be(HttpStatusCode.Created);
        var reversal = await reverseOut.Content.ReadFromJsonAsync<JsonElement>();
        reversal.GetProperty("batchId").GetGuid().Should().Be(batchId);
        reversal.GetProperty("batchBalanceAfter").GetDecimal().Should().Be(10m);
        (await RemainingAsync(batchId)).Should().Be(10m);

        // Girişi geri al: parti kalanı (10) miktarı (10) karşılıyorsa olur; kısmen kullanılmışsa olmaz.
        await PostMovementAsync(client, ctx, 2, 1m, batchId: batchId);
        var blocked = await ReverseAsync(client, entryMovementId);
        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(blocked)).Should().Be("StockMovements.InsufficientStock");
        (await RemainingAsync(batchId)).Should().Be(9m);
        (await GetStockAsync(ctx)).Should().Be(9m);
    }

    [Fact]
    public async Task Reverse_Unused_Batched_In_Should_Zero_The_Batch_Without_Deleting_It()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var entry = await (await PostMovementAsync(client, ctx, 1, 6m, batch: new { lotNumber = "U1", expiryDate = Date(100) }))
            .Content.ReadFromJsonAsync<JsonElement>();

        var reverse = await ReverseAsync(client, entry.GetProperty("id").GetGuid());

        reverse.StatusCode.Should().Be(HttpStatusCode.Created);
        (await RemainingAsync(entry.GetProperty("batchId").GetGuid())).Should().Be(0m);
        (await GetStockAsync(ctx)).Should().Be(0m);
        (await BatchCountAsync(ctx)).Should().Be(1);
    }

    // ---------------------------------------------------------------- Uygulama kaydı

    [Fact]
    public async Task Administration_Should_Deduct_From_Fefo_Batch_And_Return_Lot_And_Expiry_In_Create_Get_And_List()
    {
        var ctx = await SeedAsync(ProductKind.Vaccine);
        var client = ClientFor(ctx);
        await EnterBatchAsync(client, ctx, 10m, "LATE-LOT", Date(300));
        var soon = await EnterBatchAsync(client, ctx, 10m, "SOON-LOT", Date(40));

        var response = await PostAdministrationAsync(client, ctx, 2m);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var admin = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("administration");
        admin.GetProperty("batchId").GetGuid().Should().Be(soon);
        admin.GetProperty("lotNumber").GetString().Should().Be("SOON-LOT");
        admin.GetProperty("expiryDate").GetString().Should().Be(Date(40));
        (await RemainingAsync(soon)).Should().Be(8m);
        var movement = await OutMovementForAsync(admin.GetProperty("id").GetGuid());
        movement.BatchId.Should().Be(soon);
        movement.BatchBalanceAfter.Should().Be(8m);

        var id = admin.GetProperty("id").GetGuid();
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/administrations/{id}");
        detail.GetProperty("lotNumber").GetString().Should().Be("SOON-LOT");
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/administrations?clinicId={ctx.ClinicId}&petId={ctx.PetId}");
        list.GetProperty("items")[0].GetProperty("expiryDate").GetString().Should().Be(Date(40));
    }

    [Fact]
    public async Task Administration_With_Explicit_Batch_Should_Use_It()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        await EnterBatchAsync(client, ctx, 10m, "A", Date(30));
        var chosen = await EnterBatchAsync(client, ctx, 10m, "B", Date(300));

        var response = await PostAdministrationAsync(client, ctx, 3m, batchId: chosen);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("administration")
            .GetProperty("batchId").GetGuid().Should().Be(chosen);
        (await RemainingAsync(chosen)).Should().Be(7m);
    }

    [Fact]
    public async Task Administration_Should_Be_Blocked_For_Expired_Batch_And_Default_Selection_Should_Skip_It()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var expired = await EnterBatchAsync(client, ctx, 10m, "OLD", Date(-1));

        var explicitExpired = await PostAdministrationAsync(client, ctx, 1m, batchId: expired);
        var onlyExpiredAvailable = await PostAdministrationAsync(client, ctx, 1m);

        explicitExpired.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(explicitExpired)).Should().Be("Administrations.BatchExpired");
        onlyExpiredAvailable.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(onlyExpiredAvailable)).Should().Be("Administrations.BatchExpired");
        (await RemainingAsync(expired)).Should().Be(10m);
        (await GetStockAsync(ctx)).Should().Be(10m);
        (await AdministrationCountAsync(ctx)).Should().Be(0);

        var valid = await EnterBatchAsync(client, ctx, 5m, "VALID", Date(100));
        var skipsExpired = await PostAdministrationAsync(client, ctx, 1m);
        skipsExpired.StatusCode.Should().Be(HttpStatusCode.Created);
        (await skipsExpired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("administration")
            .GetProperty("batchId").GetGuid().Should().Be(valid);
    }

    [Fact]
    public async Task Administration_On_Expiry_Day_Should_Still_Be_Allowed()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        // İstanbul günü, UTC gününden en çok bir gün ileri olabilir: SKT = İstanbul bugünü (dün/yarın sınırında flake olmasın diye yarın).
        var tomorrow = await EnterBatchAsync(client, ctx, 3m, "EDGE", Date(1));

        var response = await PostAdministrationAsync(client, ctx, 1m, batchId: tomorrow);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Administration_Insufficient_Batch_Should_Return_InsufficientStock_Without_Creating_Record()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var a = await EnterBatchAsync(client, ctx, 3m, "A", Date(100));
        await EnterBatchAsync(client, ctx, 3m, "B", Date(200));

        var tooBigForAnyBatch = await PostAdministrationAsync(client, ctx, 5m);
        var tooBigForChosen = await PostAdministrationAsync(client, ctx, 4m, batchId: a);

        tooBigForAnyBatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(tooBigForAnyBatch)).Should().Be("Administrations.InsufficientStock");
        tooBigForChosen.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(tooBigForChosen)).Should().Be("Administrations.InsufficientStock");
        (await AdministrationCountAsync(ctx)).Should().Be(0);
        (await GetStockAsync(ctx)).Should().Be(6m);
    }

    [Fact]
    public async Task Administration_Insufficient_Should_Tell_Available_And_Expired_Excluded()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        await EnterBatchAsync(client, ctx, 3m, "OLD", Date(-10));
        await EnterBatchAsync(client, ctx, 5m, "OK", Date(100));

        var response = await PostAdministrationAsync(client, ctx, 6m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Administrations.InsufficientStock");
        problem.GetProperty("detail").GetString().Should().Contain("Yetersiz bakiye: kullanılabilir 5, süresi geçmiş 3 (hariç)");
    }

    [Fact]
    public async Task Entry_With_Past_Expiry_Should_Require_Acknowledgement_And_Create_Nothing_Without_It()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var batch = new { lotNumber = "ESKI", expiryDate = Date(-30) };

        var withoutAck = await PostMovementAsync(client, ctx, 1, 3m, batch: batch);
        var falseAck = await PostMovementAsync(client, ctx, 1, 3m, batch: batch, acknowledgeExpired: false);

        withoutAck.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(withoutAck)).Should().Be("StockBatches.ExpiredAcknowledgementRequired");
        falseAck.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BatchCountAsync(ctx)).Should().Be(0);
        (await GetStockAsync(ctx)).Should().Be(0m);

        var acknowledged = await PostMovementAsync(client, ctx, 1, 3m, batch: batch, acknowledgeExpired: true);
        acknowledged.StatusCode.Should().Be(HttpStatusCode.Created);
        (await BatchCountAsync(ctx)).Should().Be(1);
        (await GetStockAsync(ctx)).Should().Be(3m);
    }

    [Fact]
    public async Task Balance_StockStatus_And_LowStock_Should_Split_Available_From_Expired()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        await EnterBatchAsync(client, ctx, 5m, "OK", Date(100));
        await EnterBatchAsync(client, ctx, 3m, "OLD", Date(-10));

        var balance = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/products/{ctx.ProductId}/stock-batches?clinicId={ctx.ClinicId}");
        balance.GetProperty("quantityOnHand").GetDecimal().Should().Be(8m);
        balance.GetProperty("availableQuantity").GetDecimal().Should().Be(5m);
        balance.GetProperty("expiredQuantity").GetDecimal().Should().Be(3m);

        var stocks = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/product-stocks?page=1&pageSize=20&clinicId={ctx.ClinicId}&productId={ctx.ProductId}");
        var stockRow = stocks.GetProperty("items").EnumerateArray().Single();
        var stockId = stockRow.GetProperty("id").GetGuid();
        stockRow.GetProperty("quantityOnHand").GetDecimal().Should().Be(8m);
        stockRow.GetProperty("availableQuantity").GetDecimal().Should().Be(5m);
        stockRow.GetProperty("expiredQuantity").GetDecimal().Should().Be(3m);

        // Eşik 6: toplam 8 >= 6 ama kullanılabilir 5 < 6 → düşük stok.
        var put = await client.PutAsJsonAsync(
            $"/api/v1/product-stocks/{stockId}/minimum-stock-level", new { minimumStockLevel = 6m });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var putJson = await put.Content.ReadFromJsonAsync<JsonElement>();
        putJson.GetProperty("isBelowMinimum").GetBoolean().Should().BeTrue();
        putJson.GetProperty("availableQuantity").GetDecimal().Should().Be(5m);

        var below = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/product-stocks?page=1&pageSize=20&clinicId={ctx.ClinicId}&isBelowMinimum=true");
        var notBelow = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/product-stocks?page=1&pageSize=20&clinicId={ctx.ClinicId}&isBelowMinimum=false");
        below.GetProperty("items").GetArrayLength().Should().Be(1);
        notBelow.GetProperty("items").GetArrayLength().Should().Be(0);

        var byProduct = await client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{ctx.ProductId}/stocks");
        byProduct.EnumerateArray().Single().GetProperty("expiredQuantity").GetDecimal().Should().Be(3m);

        // Uygulama sonrası kalan bildirimi kullanılabiliri söyler: 5 - 1 = 4 (süresi geçmiş 3 ayrı).
        var administration = await PostAdministrationAsync(client, ctx, 1m);
        administration.StatusCode.Should().Be(HttpStatusCode.Created);
        var remaining = (await administration.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stock");
        remaining.GetProperty("quantityOnHand").GetDecimal().Should().Be(7m);
        remaining.GetProperty("availableQuantity").GetDecimal().Should().Be(4m);
        remaining.GetProperty("expiredQuantity").GetDecimal().Should().Be(3m);
        remaining.GetProperty("isBelowMinimum").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Administration_On_Legacy_Unbatched_Stock_Should_Work_With_Null_Batch()
    {
        var ctx = await SeedAsync(legacyStock: 10m);

        var response = await PostAdministrationAsync(ClientFor(ctx), ctx, 2m);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var admin = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("administration");
        admin.GetProperty("batchId").ValueKind.Should().Be(JsonValueKind.Null);
        admin.GetProperty("lotNumber").ValueKind.Should().Be(JsonValueKind.Null);
        (await GetStockAsync(ctx)).Should().Be(8m);
    }

    [Fact]
    public async Task Administration_SameRequestId_Should_Deduct_Batch_Once_And_Return_Same_Batch()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var batch = await EnterBatchAsync(client, ctx, 10m, "RQ", Date(100));
        var requestId = Guid.NewGuid();

        var first = await PostAdministrationAsync(client, ctx, 3m, requestId: requestId);
        var replay = await PostAdministrationAsync(client, ctx, 3m, requestId: requestId);
        var otherBatch = await PostAdministrationAsync(client, ctx, 3m, requestId: requestId, batchId: Guid.NewGuid());

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("administration")
            .GetProperty("batchId").GetGuid().Should().Be(batch);
        otherBatch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(otherBatch)).Should().Be("Administrations.DuplicateRequestId");
        (await RemainingAsync(batch)).Should().Be(7m);
    }

    [Fact]
    public async Task Administration_Void_Should_Return_Stock_To_Same_Batch_And_WrittenOff_Should_Not()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var batch = await EnterBatchAsync(client, ctx, 10m, "VD", Date(100));
        var returned = (await (await PostAdministrationAsync(client, ctx, 3m)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("administration").GetProperty("id").GetGuid();
        var written = (await (await PostAdministrationAsync(client, ctx, 2m)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("administration").GetProperty("id").GetGuid();
        (await RemainingAsync(batch)).Should().Be(5m);

        var voidReturned = await client.PostAsJsonAsync($"/api/v1/administrations/{returned}/void",
            new { reason = "Yanlış ürün seçildi", stockOutcome = "StockReturned" });
        var voidWritten = await client.PostAsJsonAsync($"/api/v1/administrations/{written}/void",
            new { reason = "Şişe döküldü, imha", stockOutcome = "WrittenOff" });

        voidReturned.StatusCode.Should().Be(HttpStatusCode.OK);
        (await voidReturned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lotNumber").GetString().Should().Be("VD");
        voidWritten.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RemainingAsync(batch)).Should().Be(8m); // 5 + iade 3; imha edilen 2 geri gelmez
        (await GetStockAsync(ctx)).Should().Be(8m);
    }

    // ---------------------------------------------------------------- Eşzamanlılık

    [Fact]
    public async Task Concurrent_Administrations_On_One_Batch_Should_Never_Oversell_Or_Return_500()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var batch = await EnterBatchAsync(client, ctx, 5m, "RACE", Date(100));

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            PostAdministrationAsync(client, ctx, 1m, requestId: Guid.NewGuid(), batchId: batch)));

        responses.Select(r => r.StatusCode).Should().OnlyContain(
            s => s == HttpStatusCode.Created || s == HttpStatusCode.Conflict || s == HttpStatusCode.BadRequest);
        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        created.Should().BeInRange(1, 5);
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            (await ReadProblemCodeAsync(rejected)).Should().BeOneOf(
                "StockMovements.ConcurrencyConflict", "Administrations.InsufficientStock");
        }

        (await RemainingAsync(batch)).Should().Be(5m - created);
        (await GetStockAsync(ctx)).Should().Be(5m - created);
        (await AdministrationCountAsync(ctx)).Should().Be(created);
        (await OutMovementCountAsync(ctx)).Should().Be(created);
    }

    [Fact]
    public async Task Concurrent_Manual_Outs_Should_Keep_Batch_And_Stock_Equal()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        await EnterBatchAsync(client, ctx, 6m, "M1", Date(30));
        await EnterBatchAsync(client, ctx, 6m, "M2", Date(60));

        var responses = await Task.WhenAll(Enumerable.Range(0, 14).Select(_ =>
            PostMovementAsync(client, ctx, 2, 1m, requestId: Guid.NewGuid())));

        responses.Select(r => r.StatusCode).Should().OnlyContain(
            s => s == HttpStatusCode.Created || s == HttpStatusCode.Conflict || s == HttpStatusCode.BadRequest);
        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var (sumBatches, stock) = await BatchSumAndStockAsync(ctx);
        stock.Should().Be(12m - created);
        sumBatches.Should().Be(stock); // değişmez kural: Σ parti kalanı = stok (partisiz bakiye 0)
    }

    [Fact]
    public async Task Concurrent_Same_RequestId_Entry_Should_Create_Exactly_One_Batch_And_Add_Stock_Once()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var requestId = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            PostMovementAsync(client, ctx, 1, 4m, requestId: requestId, batch: new { lotNumber = "ONCE", expiryDate = Date(100) })));

        responses.Select(r => r.StatusCode).Should().OnlyContain(
            s => s == HttpStatusCode.Created || s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
            (await ReadProblemCodeAsync(conflict)).Should().Be("StockMovements.ConcurrencyConflict");
        (await BatchCountAsync(ctx)).Should().Be(1);
        (await GetStockAsync(ctx)).Should().Be(4m);
        var (sumBatches, stock) = await BatchSumAndStockAsync(ctx);
        sumBatches.Should().Be(stock);
    }

    [Fact]
    public async Task Concurrent_Same_RequestId_Out_On_Existing_Batch_Should_Deduct_Batch_And_Stock_Once()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var batch = await EnterBatchAsync(client, ctx, 10m, "OUT1", Date(100));
        var requestId = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            PostMovementAsync(client, ctx, 2, 3m, requestId: requestId, batchId: batch)));

        responses.Select(r => r.StatusCode).Should().OnlyContain(
            s => s == HttpStatusCode.Created || s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        (await RemainingAsync(batch)).Should().Be(7m);
        (await GetStockAsync(ctx)).Should().Be(7m);
    }

    [Fact]
    public async Task Concurrent_First_Entries_With_Batches_Should_Not_Leave_Orphan_Batches()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            PostMovementAsync(client, ctx, 1, 1m, requestId: Guid.NewGuid(), batch: new { lotNumber = $"F{i}", expiryDate = Date(100) })));

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created || s == HttpStatusCode.Conflict);
        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        created.Should().BeGreaterThan(0);
        (await BatchCountAsync(ctx)).Should().Be(created); // başarısız (409) girişin partisi yazılmaz
        var (sumBatches, stock) = await BatchSumAndStockAsync(ctx);
        stock.Should().Be(created);
        sumBatches.Should().Be(stock);
    }

    // Eşzamanlı testin nadiren yakaladığı pencere: kaybeden istek ön kontrolü kazanan yazılmadan yapar, satırları kazananın
    // commit'inden sonra güncel okur. Yazımı benzersiz kurala çarpınca kendi stok/parti değişikliği işlemde kalmamalı.

    [Fact]
    public async Task Same_RequestId_Out_That_Missed_Winner_In_PreCheck_Should_Replay_Without_Deducting_Again()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var batch = await EnterBatchAsync(client, ctx, 10m, "RACE", Date(100));
        var requestId = Guid.NewGuid();
        (await PostMovementAsync(client, ctx, 2, 3m, requestId: requestId, batchId: batch)).StatusCode
            .Should().Be(HttpStatusCode.Created);

        await using var racing = PreCheckMissingHost();
        var loser = await PostMovementAsync(ClientFor(ctx, racing), ctx, 2, 3m, requestId: requestId, batchId: batch);

        loser.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RemainingAsync(batch)).Should().Be(7m);
        (await GetStockAsync(ctx)).Should().Be(7m);
    }

    [Fact]
    public async Task Same_RequestId_Reverse_That_Missed_Winner_In_PreCheck_Should_Replay_Without_Restoring_Again()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);
        var batch = await EnterBatchAsync(client, ctx, 10m, "RRACE", Date(100));
        var outMovement = await (await PostMovementAsync(client, ctx, 2, 3m, batchId: batch)).Content.ReadFromJsonAsync<JsonElement>();
        var outId = outMovement.GetProperty("id").GetGuid();
        var requestId = Guid.NewGuid();
        (await ReverseAsync(client, outId, requestId)).StatusCode.Should().Be(HttpStatusCode.Created);

        await using var racing = PreCheckMissingHost();
        var loser = await ReverseAsync(ClientFor(ctx, racing), outId, requestId);

        loser.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RemainingAsync(batch)).Should().Be(10m);
        (await GetStockAsync(ctx)).Should().Be(10m);
    }

    // ---------------------------------------------------------------- Ürün alanları, yetki, kapsam

    [Fact]
    public async Task Product_Put_Without_Class_Fields_Should_Not_Reset_Them_And_Get_Should_Return_Them()
    {
        var ctx = await SeedAsync();
        var client = ClientFor(ctx);

        var set = await client.PutAsJsonAsync($"/api/v1/products/{ctx.ProductId}", new
        {
            id = ctx.ProductId, name = "Ketamin", unit = "Şişe", unitPrice = 5, currency = "TRY",
            kind = "Medicine", controlledClass = "Narcotic", pharmacologicalForm = "Enjeksiyonluk çözelti",
            presentationForm = "Flakon", packaging = "10 ml",
        });
        var oldClientPut = await client.PutAsJsonAsync($"/api/v1/products/{ctx.ProductId}", new
        {
            id = ctx.ProductId, name = "Ketamin 2", unit = "Şişe", unitPrice = 6, currency = "TRY",
        });

        set.StatusCode.Should().Be(HttpStatusCode.NoContent);
        oldClientPut.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var product = await client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{ctx.ProductId}");
        product.GetProperty("name").GetString().Should().Be("Ketamin 2");
        product.GetProperty("kind").GetString().Should().Be("Medicine");
        product.GetProperty("controlledClass").GetString().Should().Be("Narcotic");
        product.GetProperty("pharmacologicalForm").GetString().Should().Be("Enjeksiyonluk çözelti");
        product.GetProperty("presentationForm").GetString().Should().Be("Flakon");
        product.GetProperty("packaging").GetString().Should().Be("10 ml");

        // Sınıf atandıktan sonra giriş lot/SKT ister.
        (await PostMovementAsync(client, ctx, 1, 1m)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Batch_Endpoints_Should_Require_Read_Permission_Clinic_Scope_And_Hide_Other_Tenants()
    {
        var ctx = await SeedAsync();
        var other = await SeedAsync();
        var batch = await EnterBatchAsync(ClientFor(ctx), ctx, 5m, "SEC", Date(100));

        var noPermission = await SeedClientAsync(ctx, new[] { "Products.Read" });
        (await noPermission.GetAsync($"/api/v1/stock-batches?clinicId={ctx.ClinicId}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        var noClinicClaim = await SeedClientAsync(ctx, new[] { "StockMovements.Read" }, includeClinicClaim: false);
        var scopeless = await noClinicClaim.GetAsync("/api/v1/stock-batches");
        scopeless.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(scopeless)).Should().Be("StockBatches.ClinicScopeRequired");
        (await noClinicClaim.GetAsync($"/api/v1/products/{ctx.ProductId}/stock-batches")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        var otherTenant = ClientFor(other);
        (await otherTenant.GetAsync($"/api/v1/stock-batches/{batch}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otherTenant.GetAsync($"/api/v1/products/{ctx.ProductId}/stock-batches?clinicId={other.ClinicId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        var foreignList = await otherTenant.GetFromJsonAsync<JsonElement>($"/api/v1/stock-batches?clinicId={other.ClinicId}");
        foreignList.GetProperty("items").GetArrayLength().Should().Be(0);
        (await ClientFor(ctx).GetAsync("/api/v1/stock-batches/" + Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- Yardımcılar

    private HttpClient ClientFor(SeedCtx ctx, WebApplicationFactory<global::Program>? host = null)
    {
        var client = (host ?? _factory).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        return client;
    }

    private async Task<HttpClient> SeedClientAsync(SeedCtx ctx, string[] permissions, bool includeClinicClaim = true)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, ctx.TenantId, ctx.ClinicId, permissions, includeClinicClaimInJwt: includeClinicClaim);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> PostMovementAsync(
        HttpClient client,
        SeedCtx ctx,
        int movementType,
        decimal quantity,
        Guid? requestId = null,
        object? batch = null,
        Guid? batchId = null,
        bool? acknowledgeExpired = null)
        => client.PostAsJsonAsync("/api/v1/stock-movements", new
        {
            clinicId = ctx.ClinicId,
            productId = ctx.ProductId,
            movementType,
            quantity,
            requestId,
            batch,
            batchId,
            acknowledgeExpired,
        });

    private static Task<HttpResponseMessage> ReverseAsync(HttpClient client, Guid movementId, Guid? requestId = null)
        => client.PostAsJsonAsync($"/api/v1/stock-movements/{movementId}/reverse", new { reason = "Yanlış giriş", requestId });

    /// <summary>
    /// requestId ve ters hareket ön kontrolünü (istek başına ilk sorgu) kör eden host; kazananı sonradan arayan sorgu gerçeği görür.
    /// </summary>
    private WebApplicationFactory<global::Program> PreCheckMissingHost()
        => _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddScoped<IRepository<StockMovement>, PreCheckMissingStockMovementRepository>()));

    private sealed class PreCheckMissingStockMovementRepository(AppDbContext db) : EfRepository<StockMovement>(db)
    {
        private readonly HashSet<Type> _missed = new();

        public override Task<StockMovement?> FirstOrDefaultAsync(
            ISpecification<StockMovement> specification, CancellationToken cancellationToken = default)
            => specification is StockMovementByRequestIdSpec or StockMovementReversalOfSpec && _missed.Add(specification.GetType())
                ? Task.FromResult<StockMovement?>(null)
                : base.FirstOrDefaultAsync(specification, cancellationToken);
    }

    private static Task<HttpResponseMessage> PostAdministrationAsync(
        HttpClient client, SeedCtx ctx, decimal quantity, Guid? requestId = null, Guid? batchId = null)
        => client.PostAsJsonAsync("/api/v1/administrations", new
        {
            clinicId = ctx.ClinicId,
            petId = ctx.PetId,
            productId = ctx.ProductId,
            quantity,
            requestId,
            batchId,
        });

    /// <summary>Yeni parti girer ve kimliğini döndürür.</summary>
    private static async Task<Guid> EnterBatchAsync(HttpClient client, SeedCtx ctx, decimal quantity, string lot, string? expiry)
    {
        // Geçmiş SKT'li girişler açık onayla yapılır (ADR-015 karar 6).
        var response = await PostMovementAsync(
            client, ctx, 1, quantity, batch: new { lotNumber = lot, expiryDate = expiry }, acknowledgeExpired: true);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("batchId").GetGuid();
    }

    private static async Task<List<Guid>> ListIdsAsync(HttpClient client, SeedCtx ctx, string extraQuery)
    {
        var json = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/stock-batches?clinicId={ctx.ClinicId}&productId={ctx.ProductId}&pageSize=50{extraQuery}");
        return json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<decimal> RemainingAsync(Guid batchId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.StockBatches.AsNoTracking().Where(b => b.Id == batchId).Select(b => b.RemainingQuantity).SingleAsync();
    }

    private async Task<decimal> GetStockAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProductStocks.AsNoTracking()
            .Where(s => s.ClinicId == ctx.ClinicId && s.ProductId == ctx.ProductId)
            .Select(s => s.QuantityOnHand)
            .SingleOrDefaultAsync();
    }

    private async Task<int> BatchCountAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.StockBatches.AsNoTracking().CountAsync(b => b.ProductId == ctx.ProductId);
    }

    private async Task<(decimal SumBatches, decimal Stock)> BatchSumAndStockAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sum = await db.StockBatches.AsNoTracking()
            .Where(b => b.ProductId == ctx.ProductId).Select(b => b.RemainingQuantity).ToListAsync();
        var stock = await db.ProductStocks.AsNoTracking()
            .Where(s => s.ProductId == ctx.ProductId).Select(s => s.QuantityOnHand).SingleOrDefaultAsync();
        return (sum.Sum(), stock);
    }

    private async Task<int> AdministrationCountAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Administrations.AsNoTracking().CountAsync(a => a.ProductId == ctx.ProductId);
    }

    private async Task<int> OutMovementCountAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.StockMovements.AsNoTracking()
            .CountAsync(m => m.ProductId == ctx.ProductId && m.MovementType == StockMovementType.Out);
    }

    private async Task<StockMovement> OutMovementForAsync(Guid administrationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.StockMovements.AsNoTracking()
            .SingleAsync(m => m.ReferenceId == administrationId && m.MovementType == StockMovementType.Out);
    }

    private async Task<SeedCtx> SeedAsync(
        ProductKind kind = ProductKind.Other,
        ControlledSubstanceClass controlled = ControlledSubstanceClass.None,
        decimal? legacyStock = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"bt-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Bt-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var product = new Product(
            tenant.Id, $"bp-{Guid.NewGuid():N}"[..14], "Doz", 2m, "TRY", DateTime.UtcNow, sku: $"BSK-{Guid.NewGuid():N}"[..14],
            kind: kind, controlledClass: controlled);
        db.Products.Add(product);
        var client = new Client(tenant.Id, $"BtClient-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110099");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, $"BtPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        if (legacyStock is { } onHand)
            db.ProductStocks.Add(new ProductStock(tenant.Id, clinic.Id, product.Id, onHand, 0, DateTime.UtcNow));
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, AllPermissions, includeClinicClaimInJwt: true);

        return new SeedCtx(tenant.Id, clinic.Id, product.Id, pet.Id, token);
    }

    private sealed record SeedCtx(Guid TenantId, Guid ClinicId, Guid ProductId, Guid PetId, string Token);
}
