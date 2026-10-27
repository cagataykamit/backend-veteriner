using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Sales;

/// <summary>
/// Doğrudan satış (ADR-016): gerçek SQL Server/LocalDB üzerinde stok düşümü + tek ücret + ödeme atomikliği, Peşin müşteri kuralları,
/// aşı/kontrollü ürün engeli, süresi geçmiş parti, requestId tekrarı/yarışı, son ürün yarışı ve iptal (stok iadesi/zarar, ödeme/ücret iptali).
/// </summary>
[Collection("products-api")]
public sealed class SalesEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AllPermissions = { "Sales.Create", "Sales.Read", "Sales.Correct" };

    private readonly CustomWebApplicationFactory _factory;

    public SalesEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ---------- yetki ----------

    [Fact]
    public async Task Post_Should_Return401_When_TokenMissing()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/sales", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_And_Void_Should_Return403_Without_Their_Permissions_And_Write_Nothing()
    {
        var ctx = await SeedAsync(stock: 10m);
        var readOnly = await SeedUserClientAsync(ctx, new[] { "Sales.Read" });
        var creatorOnly = await SeedUserClientAsync(ctx, new[] { "Sales.Create", "Sales.Read" });

        (await PostAsync(readOnly, ctx, Lines(ctx.ProductId, 1m), payment: Cash())).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var created = await PostAsync(creatorOnly, ctx, Lines(ctx.ProductId, 1m), payment: Cash());
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var saleId = await SaleIdAsync(created);

        // Sekreter düzeyi (Create+Read) iptal edemez.
        (await creatorOnly.PostAsJsonAsync($"/api/v1/sales/{saleId}/void", new { reason = "Yanlış satış kaydı" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await GetStockAsync(ctx)).Should().Be(9m);
        (await SaleCountAsync(ctx)).Should().Be(1);
    }

    // ---------- oluşturma ----------

    [Fact]
    public async Task Post_WalkIn_Should_Drop_Stock_Write_Single_Charge_And_Full_Payment()
    {
        var ctx = await SeedAsync(stock: 10m, minimum: 8m);
        var client = ClientFor(ctx);

        var response = await PostAsync(client, ctx, Lines(ctx.ProductId, 2m), payment: Cash());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("created").GetBoolean().Should().BeTrue();
        var sale = json.GetProperty("sale");
        sale.GetProperty("status").GetString().Should().Be("Active");
        sale.GetProperty("isWalkIn").GetBoolean().Should().BeTrue();
        sale.GetProperty("clientName").GetString().Should().Be("Peşin müşteri");
        sale.GetProperty("totalAmount").GetDecimal().Should().Be(20.00m);
        sale.GetProperty("paidAmount").GetDecimal().Should().Be(20.00m);
        sale.GetProperty("outstandingAmount").GetDecimal().Should().Be(0m);
        var line = sale.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("quantity").GetDecimal().Should().Be(2m);
        line.GetProperty("unitPrice").GetDecimal().Should().Be(10.00m);
        line.GetProperty("lineAmount").GetDecimal().Should().Be(20.00m);
        line.GetProperty("productName").GetString().Should().NotBeNullOrEmpty();
        var stock = json.GetProperty("stocks").EnumerateArray().Single();
        stock.GetProperty("quantityOnHand").GetDecimal().Should().Be(8m);
        stock.GetProperty("isBelowMinimum").GetBoolean().Should().BeFalse();

        var saleId = sale.GetProperty("id").GetGuid();
        (await GetStockAsync(ctx)).Should().Be(8m);
        var lineId = line.GetProperty("id").GetGuid();
        var movement = (await MovementsForAsync(lineId)).Should().ContainSingle().Subject;
        movement.MovementType.Should().Be(StockMovementType.Out);
        movement.ReferenceType.Should().Be("SaleLine");
        movement.Quantity.Should().Be(2m);

        var charges = await ChargesAsync(ctx);
        var charge = charges.Should().ContainSingle().Subject;
        charge.Description.Should().Be("Ürün satışı");
        charge.Amount.Should().Be(20.00m);
        charge.AllocatedAmount.Should().Be(20.00m);
        charge.RequestId.Should().Be(saleId);
        (await PaymentCountAsync(ctx)).Should().Be(1);

        // Aynı kiracıda ikinci peşin satış aynı peşin müşteriyi kullanır.
        var second = await PostAsync(client, ctx, Lines(ctx.ProductId, 1m), payment: Cash());
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        (await WalkInClientCountAsync(ctx)).Should().Be(1);
    }

    [Fact]
    public async Task Post_Should_Use_Product_Price_By_Default_And_Allow_Edited_Price_And_Multiple_Lines()
    {
        var ctx = await SeedAsync(stock: 10m);

        // 2 × 10.00 (ürün fiyatı) + 0.333 × 10.05 (düzenlenmiş fiyat; 3.34665 → 3.35) + 3 × 25.50 (ikinci ürün)
        var response = await PostAsync(
            ClientFor(ctx),
            ctx,
            new object[]
            {
                new { productId = ctx.ProductId, quantity = 2m },
                new { productId = ctx.ProductId, quantity = 0.333m, unitPrice = 10.05m },
                new { productId = ctx.Product2Id, quantity = 3m },
            },
            payment: Cash());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var sale = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sale");
        sale.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("lineAmount").GetDecimal())
            .Should().Equal(20.00m, 3.35m, 76.50m);
        sale.GetProperty("totalAmount").GetDecimal().Should().Be(99.85m);
        (await ChargesAsync(ctx)).Should().ContainSingle().Which.Amount.Should().Be(99.85m);
        (await GetStockAsync(ctx)).Should().Be(7.667m);
        (await GetStockAsync(ctx, ctx.Product2Id)).Should().Be(14m);
        (await OutMovementCountAsync(ctx)).Should().Be(3);
    }

    [Fact]
    public async Task Post_WalkIn_WithoutPayment_Or_Partial_Payment_Should_Fail_And_Leave_No_Record()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);

        var none = await PostAsync(client, ctx, Lines(ctx.ProductId, 1m));
        none.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(none)).Should().Be("Sales.WalkInPaymentRequired");

        var partial = await PostAsync(client, ctx, Lines(ctx.ProductId, 2m), payment: new { method = "Cash", amount = 5m });
        partial.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(partial)).Should().Be("Sales.WalkInPaymentMustBeFull");

        (await GetStockAsync(ctx)).Should().Be(10m);
        (await SaleCountAsync(ctx)).Should().Be(0);
        (await ChargesAsync(ctx)).Should().BeEmpty();
        (await PaymentCountAsync(ctx)).Should().Be(0);
        (await OutMovementCountAsync(ctx)).Should().Be(0);
        // Hata öncesi aynı işlemde oluşan peşin müşteri de geri alınır.
        (await WalkInClientCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task Post_WithClient_Should_Allow_Credit_Partial_Payment_And_Reject_Overpayment()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);

        var credit = await PostAsync(client, ctx, Lines(ctx.ProductId, 3m), clientId: ctx.ClientId);
        credit.StatusCode.Should().Be(HttpStatusCode.Created);
        var creditSale = (await credit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sale");
        creditSale.GetProperty("isWalkIn").GetBoolean().Should().BeFalse();
        creditSale.GetProperty("paymentId").ValueKind.Should().Be(JsonValueKind.Null);
        creditSale.GetProperty("paidAmount").GetDecimal().Should().Be(0m);
        creditSale.GetProperty("outstandingAmount").GetDecimal().Should().Be(30.00m);

        var partial = await PostAsync(client, ctx, Lines(ctx.ProductId, 2m), clientId: ctx.ClientId, payment: new { method = "Card", amount = 5m });
        partial.StatusCode.Should().Be(HttpStatusCode.Created);
        var partialSale = (await partial.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sale");
        partialSale.GetProperty("paidAmount").GetDecimal().Should().Be(5.00m);
        partialSale.GetProperty("outstandingAmount").GetDecimal().Should().Be(15.00m);

        var over = await PostAsync(client, ctx, Lines(ctx.ProductId, 1m), clientId: ctx.ClientId, payment: new { method = "Cash", amount = 10.01m });
        over.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(over)).Should().Be("Sales.PaymentExceedsTotal");

        var unknownClient = await PostAsync(client, ctx, Lines(ctx.ProductId, 1m), clientId: Guid.NewGuid(), payment: Cash());
        unknownClient.StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await GetStockAsync(ctx)).Should().Be(5m);
        (await SaleCountAsync(ctx)).Should().Be(2);
        (await WalkInClientCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task Post_Vaccine_And_Controlled_Products_Should_Be_Blocked_With_Distinct_Codes_And_No_Side_Effects()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);

        var vaccine = await PostAsync(client, ctx, Lines(ctx.VaccineProductId, 1m), payment: Cash());
        vaccine.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(vaccine)).Should().Be("Sales.VaccineNotSellable");

        var narcotic = await PostAsync(client, ctx, Lines(ctx.ControlledProductId, 1m), payment: Cash());
        narcotic.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(narcotic)).Should().Be("Sales.ControlledSubstanceNotSellable");

        // Sepette tek bir yasaklı ürün tüm satışı reddeder; sıradan satır da yazılmaz.
        var mixed = await PostAsync(
            client,
            ctx,
            new object[] { new { productId = ctx.ProductId, quantity = 1m }, new { productId = ctx.VaccineProductId, quantity = 1m } },
            payment: Cash());
        mixed.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await GetStockAsync(ctx)).Should().Be(10m);
        (await SaleCountAsync(ctx)).Should().Be(0);
        (await ChargesAsync(ctx)).Should().BeEmpty();
    }

    [Fact]
    public async Task Post_InsufficientStock_On_Second_Line_Should_Roll_Back_First_Line_And_Everything_Else()
    {
        var ctx = await SeedAsync(stock: 10m);

        var response = await PostAsync(
            ClientFor(ctx),
            ctx,
            new object[] { new { productId = ctx.ProductId, quantity = 4m }, new { productId = ctx.Product2Id, quantity = 99m } },
            payment: Cash());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("Sales.InsufficientStock");
        (await GetStockAsync(ctx)).Should().Be(10m);
        (await GetStockAsync(ctx, ctx.Product2Id)).Should().Be(17m);
        (await SaleCountAsync(ctx)).Should().Be(0);
        (await OutMovementCountAsync(ctx)).Should().Be(0);
        (await ChargesAsync(ctx)).Should().BeEmpty();
        (await PaymentCountAsync(ctx)).Should().Be(0);
        (await WalkInClientCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task Post_Should_Reject_Invalid_Input_Without_Side_Effects()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);

        (await PostAsync(client, ctx, Lines(ctx.ProductId, 0m), payment: Cash())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsync(client, ctx, Lines(ctx.ProductId, 1.0001m), payment: Cash())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsync(client, ctx, new object[] { new { productId = ctx.ProductId, quantity = 1m, unitPrice = 0m } }, payment: Cash()))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsync(client, ctx, Array.Empty<object>(), payment: Cash())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsync(client, ctx, Lines(Guid.NewGuid(), 1m), payment: Cash())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostAsync(client, ctx, Lines(ctx.ProductId, 1m), payment: new { method = "Bitcoin" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsync(client, ctx, Lines(ctx.ProductId, 1m), payment: Cash(), requestId: Guid.Empty)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await DeactivateProductAsync(ctx.ProductId);
        var inactive = await PostAsync(client, ctx, Lines(ctx.ProductId, 1m), payment: Cash());
        inactive.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(inactive)).Should().Be("Sales.ProductInactive");

        (await GetStockAsync(ctx)).Should().Be(10m);
        (await SaleCountAsync(ctx)).Should().Be(0);
        (await ChargesAsync(ctx)).Should().BeEmpty();
    }

    // ---------- parti / FEFO / süresi geçmiş ----------

    [Fact]
    public async Task Post_Should_Pick_Fefo_Batch_Skip_Expired_One_And_Link_Batch_To_Line()
    {
        var ctx = await SeedAsync(stock: null);
        var expired = await SeedBatchAsync(ctx, 5m, "OLD", Today(-1));
        var soon = await SeedBatchAsync(ctx, 10m, "SOON", Today(30));
        var later = await SeedBatchAsync(ctx, 10m, "LATER", Today(300));

        var fefo = await PostAsync(ClientFor(ctx), ctx, Lines(ctx.ProductId, 3m), payment: Cash());
        fefo.StatusCode.Should().Be(HttpStatusCode.Created);
        var line = (await fefo.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sale").GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("batchId").GetGuid().Should().Be(soon);
        line.GetProperty("lotNumber").GetString().Should().Be("SOON");

        var chosen = await PostAsync(ClientFor(ctx), ctx, new object[] { new { productId = ctx.ProductId, quantity = 2m, batchId = later } }, payment: Cash());
        chosen.StatusCode.Should().Be(HttpStatusCode.Created);

        (await RemainingAsync(expired)).Should().Be(5m);
        (await RemainingAsync(soon)).Should().Be(7m);
        (await RemainingAsync(later)).Should().Be(8m);
    }

    [Fact]
    public async Task Post_Should_Block_Expired_Batch_Explicit_Or_Only_Available_With_Clear_Codes()
    {
        var ctx = await SeedAsync(stock: null);
        var expired = await SeedBatchAsync(ctx, 10m, "OLD", Today(-1));
        var client = ClientFor(ctx);

        var explicitExpired = await PostAsync(client, ctx, new object[] { new { productId = ctx.ProductId, quantity = 1m, batchId = expired } }, payment: Cash());
        var onlyExpired = await PostAsync(client, ctx, Lines(ctx.ProductId, 1m), payment: Cash());

        explicitExpired.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(explicitExpired)).Should().Be("Sales.BatchExpired");
        onlyExpired.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(onlyExpired)).Should().Be("Sales.BatchExpired");
        (await RemainingAsync(expired)).Should().Be(10m);
        (await SaleCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task Post_InsufficientAvailable_Should_Explain_Expired_Quantity_In_Message()
    {
        var ctx = await SeedAsync(stock: null);
        await SeedBatchAsync(ctx, 4m, "OLD", Today(-1));
        await SeedBatchAsync(ctx, 2m, "OK", Today(90));

        var response = await PostAsync(ClientFor(ctx), ctx, Lines(ctx.ProductId, 5m), payment: Cash());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Sales.InsufficientStock");
        problem.GetProperty("detail").GetString().Should().Contain("kullanılabilir 2").And.Contain("süresi geçmiş 4");
    }

    // ---------- requestId ----------

    [Fact]
    public async Task Post_SameRequestId_Should_Replay_Without_Second_Write_And_Reject_Different_Content()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var requestId = Guid.NewGuid();

        var first = await PostAsync(client, ctx, Lines(ctx.ProductId, 2m), payment: Cash(), requestId: requestId);
        var second = await PostAsync(client, ctx, Lines(ctx.ProductId, 2m), payment: Cash(), requestId: requestId);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondJson = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondJson.GetProperty("created").GetBoolean().Should().BeFalse();
        secondJson.GetProperty("stocks").EnumerateArray().Single().GetProperty("quantityOnHand").GetDecimal().Should().Be(8m);
        (await SaleIdAsync(first)).Should().Be(secondJson.GetProperty("sale").GetProperty("id").GetGuid());

        var different = await PostAsync(client, ctx, Lines(ctx.ProductId, 3m), payment: Cash(), requestId: requestId);
        different.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(different)).Should().Be("Sales.DuplicateRequestId");

        (await GetStockAsync(ctx)).Should().Be(8m);
        (await SaleCountAsync(ctx)).Should().Be(1);
        (await ChargesAsync(ctx)).Should().HaveCount(1);
        (await PaymentCountAsync(ctx)).Should().Be(1);
        (await OutMovementCountAsync(ctx)).Should().Be(1);
    }

    [Fact]
    public async Task Post_Concurrent_SameRequestId_Should_Never500_And_Write_Everything_Once()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var requestId = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => PostAsync(client, ctx, Lines(ctx.ProductId, 2m), payment: Cash(), requestId: requestId)));

        responses.Should().OnlyContain(r => (int)r.StatusCode < 500);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        (await GetStockAsync(ctx)).Should().Be(8m);
        (await SaleCountAsync(ctx)).Should().Be(1);
        (await ChargesAsync(ctx)).Should().HaveCount(1);
        (await PaymentCountAsync(ctx)).Should().Be(1);
        (await OutMovementCountAsync(ctx)).Should().Be(1);
        (await WalkInClientCountAsync(ctx)).Should().Be(1);
    }

    [Fact]
    public async Task Post_Concurrent_Different_Requests_For_Last_Unit_Should_Sell_It_Only_Once()
    {
        var ctx = await SeedAsync(stock: 1m);
        var client = ClientFor(ctx);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => PostAsync(client, ctx, Lines(ctx.ProductId, 1m), payment: Cash())));

        responses.Should().OnlyContain(r => (int)r.StatusCode < 500);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.BadRequest || r.StatusCode == HttpStatusCode.Conflict);
        (await GetStockAsync(ctx)).Should().Be(0m);
        (await SaleCountAsync(ctx)).Should().Be(1);
        (await ChargesAsync(ctx)).Should().HaveCount(1);
        (await PaymentCountAsync(ctx)).Should().Be(1);
        (await OutMovementCountAsync(ctx)).Should().Be(1);
    }

    // ---------- iptal ----------

    [Fact]
    public async Task Void_StockReturned_Should_Return_Stock_Void_Payment_And_Charge_And_Be_Once_Only()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var requestId = Guid.NewGuid();
        var created = await PostAsync(client, ctx, Lines(ctx.ProductId, 4m), payment: Cash(), requestId: requestId);
        var saleId = await SaleIdAsync(created);
        (await GetStockAsync(ctx)).Should().Be(6m);

        var voided = await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/void", new { reason = "Müşteri vazgeçti" });

        voided.StatusCode.Should().Be(HttpStatusCode.OK);
        var sale = await voided.Content.ReadFromJsonAsync<JsonElement>();
        sale.GetProperty("status").GetString().Should().Be("Voided");
        sale.GetProperty("voidStockOutcome").GetString().Should().Be("StockReturned");
        sale.GetProperty("voidReason").GetString().Should().Be("Müşteri vazgeçti");
        sale.GetProperty("paidAmount").GetDecimal().Should().Be(0m);
        sale.GetProperty("outstandingAmount").GetDecimal().Should().Be(0m);
        (await GetStockAsync(ctx)).Should().Be(10m);
        (await ChargesAsync(ctx)).Should().ContainSingle().Which.IsVoided.Should().BeTrue();
        (await PaymentsAsync(ctx)).Should().ContainSingle().Which.IsVoided.Should().BeTrue();
        (await ReversalCountAsync(ctx)).Should().Be(1);

        var again = await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/void", new { reason = "Müşteri vazgeçti" });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(again)).Should().Be("Sales.AlreadyVoided");
        (await GetStockAsync(ctx)).Should().Be(10m);

        var replay = await PostAsync(client, ctx, Lines(ctx.ProductId, 4m), payment: Cash(), requestId: requestId);
        replay.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(replay)).Should().Be("Sales.RequestIdVoided");
    }

    [Fact]
    public async Task Void_WrittenOff_Should_Not_Return_Stock_But_Void_Payment_And_Charge()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var saleId = await SaleIdAsync(await PostAsync(client, ctx, Lines(ctx.ProductId, 4m), payment: Cash()));

        var voided = await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/void", new { reason = "Ürün hasarlı iade", stockOutcome = "WrittenOff" });

        voided.StatusCode.Should().Be(HttpStatusCode.OK);
        (await voided.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("voidStockOutcome").GetString().Should().Be("WrittenOff");
        (await GetStockAsync(ctx)).Should().Be(6m);
        (await ReversalCountAsync(ctx)).Should().Be(0);
        (await ChargesAsync(ctx)).Should().ContainSingle().Which.IsVoided.Should().BeTrue();
        (await PaymentsAsync(ctx)).Should().ContainSingle().Which.IsVoided.Should().BeTrue();
    }

    [Fact]
    public async Task Void_Should_Return_Batch_Stock_To_Same_Batch()
    {
        var ctx = await SeedAsync(stock: null);
        var batch = await SeedBatchAsync(ctx, 10m, "B1", Today(60));
        var client = ClientFor(ctx);
        var saleId = await SaleIdAsync(await PostAsync(client, ctx, Lines(ctx.ProductId, 6m), payment: Cash()));
        (await RemainingAsync(batch)).Should().Be(4m);

        (await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/void", new { reason = "Yanlış kalem satıldı" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await RemainingAsync(batch)).Should().Be(10m);
        (await GetStockAsync(ctx)).Should().Be(10m);
    }

    [Fact]
    public async Task Void_Should_Reject_Short_Reason_And_Unknown_Outcome_And_Unknown_Sale_Without_Side_Effects()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var saleId = await SaleIdAsync(await PostAsync(client, ctx, Lines(ctx.ProductId, 1m), payment: Cash()));

        var shortReason = await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/void", new { reason = "abc" });
        shortReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var badOutcome = await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/void", new { reason = "Geçerli gerekçe", stockOutcome = "Lost" });
        badOutcome.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var unknown = await client.PostAsJsonAsync($"/api/v1/sales/{Guid.NewGuid()}/void", new { reason = "Geçerli gerekçe" });
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await GetStockAsync(ctx)).Should().Be(9m);
        (await ChargesAsync(ctx)).Should().ContainSingle().Which.IsVoided.Should().BeFalse();
        (await GetSaleStatusAsync(client, saleId)).Should().Be("Active");
    }

    [Fact]
    public async Task Void_Should_Be_Blocked_When_Credit_Sale_Charge_Was_Paid_Later_And_Roll_Back_Everything()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var created = await PostAsync(client, ctx, Lines(ctx.ProductId, 3m), clientId: ctx.ClientId);
        var sale = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sale");
        var saleId = sale.GetProperty("id").GetGuid();
        var chargeId = sale.GetProperty("chargeId").GetGuid();

        // Veresiye satışın ücreti sonradan ayrı bir ödemeyle karşılanır.
        var payer = await SeedUserClientAsync(ctx, new[] { "Payments.Create" });
        var pay = await payer.PostAsJsonAsync("/api/v1/payments", new
        {
            clinicId = ctx.ClinicId,
            clientId = ctx.ClientId,
            amount = 30m,
            currency = "TRY",
            method = 0,
            paidAtUtc = DateTime.UtcNow,
            requestId = Guid.NewGuid(),
            allocations = new[] { new { chargeId, amount = 30m } },
        });
        pay.StatusCode.Should().Be(HttpStatusCode.Created);

        var voided = await client.PostAsJsonAsync($"/api/v1/sales/{saleId}/void", new { reason = "Yanlış müşteriye yazıldı" });

        voided.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(voided)).Should().Be("Charges.VoidBlockedByAllocation");
        (await GetSaleStatusAsync(client, saleId)).Should().Be("Active");
        (await GetStockAsync(ctx)).Should().Be(7m);
        (await ReversalCountAsync(ctx)).Should().Be(0);
        (await ChargesAsync(ctx)).Should().ContainSingle().Which.IsVoided.Should().BeFalse();
    }

    [Fact]
    public async Task Void_Concurrent_Should_Succeed_Once_And_Return_Stock_Once()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var saleId = await SaleIdAsync(await PostAsync(client, ctx, Lines(ctx.ProductId, 4m), payment: Cash()));

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => client.PostAsJsonAsync($"/api/v1/sales/{saleId}/void", new { reason = "Eşzamanlı iptal denemesi" })));

        responses.Should().OnlyContain(r => (int)r.StatusCode < 500);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.OK).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        (await GetStockAsync(ctx)).Should().Be(10m);
        (await ReversalCountAsync(ctx)).Should().Be(1);
    }

    // ---------- okuma ----------

    [Fact]
    public async Task Get_And_List_Should_Filter_Hide_Voided_And_Respect_Tenant_Scope()
    {
        var ctx = await SeedAsync(stock: 20m);
        var client = ClientFor(ctx);
        var walkInSale = await SaleIdAsync(await PostAsync(client, ctx, Lines(ctx.ProductId, 1m), payment: Cash()));
        var clientSale = await SaleIdAsync(await PostAsync(client, ctx, Lines(ctx.Product2Id, 1m), clientId: ctx.ClientId));
        var voidedSale = await SaleIdAsync(await PostAsync(client, ctx, Lines(ctx.ProductId, 1m), payment: Cash()));
        (await client.PostAsJsonAsync($"/api/v1/sales/{voidedSale}/void", new { reason = "Yanlış satış kaydı" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await client.GetAsync($"/api/v1/sales/{clientSale}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        (await detail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lines").GetArrayLength().Should().Be(1);

        (await ListIdsAsync(client, ctx, "")).Should().BeEquivalentTo(new[] { walkInSale, clientSale });
        (await ListIdsAsync(client, ctx, "&includeVoided=true")).Should().BeEquivalentTo(new[] { walkInSale, clientSale, voidedSale });
        (await ListIdsAsync(client, ctx, $"&clientId={ctx.ClientId:D}")).Should().BeEquivalentTo(new[] { clientSale });
        (await ListIdsAsync(client, ctx, $"&productId={ctx.Product2Id:D}")).Should().BeEquivalentTo(new[] { clientSale });
        (await ListIdsAsync(client, ctx, $"&fromUtc={Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(5).ToString("O"))}")).Should().BeEmpty();
        (await ListIdsAsync(client, ctx, $"&toUtc={Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(5).ToString("O"))}")).Should().HaveCount(2);

        var other = await SeedAsync(stock: 5m);
        (await ClientFor(other).GetAsync($"/api/v1/sales/{clientSale}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListIdsAsync(ClientFor(other), other, "")).Should().BeEmpty();
        (await client.GetAsync($"/api/v1/sales/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var scopeless = await SeedUserClientAsync(ctx, new[] { "Sales.Read" }, includeClinicClaim: false);
        (await scopeless.GetAsync("/api/v1/sales")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- Peşin müşteri ----------

    [Fact]
    public async Task WalkInClient_Endpoint_Should_Be_Idempotent_Under_Race_And_Client_Should_Be_ReadOnly()
    {
        var ctx = await SeedAsync(stock: 1m);
        var client = ClientFor(ctx);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.PostAsync("/api/v1/sales/walk-in-client", null)));

        responses.Should().OnlyContain(r => (int)r.StatusCode < 500);
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.OK);
        responses.Where(r => r.StatusCode != HttpStatusCode.OK).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        (await WalkInClientCountAsync(ctx)).Should().Be(1);

        var again = await client.PostAsync("/api/v1/sales/walk-in-client", null);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await again.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("created").GetBoolean().Should().BeFalse();
        body.GetProperty("fullName").GetString().Should().Be("Peşin müşteri");
        var walkInId = body.GetProperty("clientId").GetGuid();

        var updater = await SeedUserClientAsync(ctx, new[] { "Clients.Update" });
        var update = await updater.PutAsJsonAsync($"/api/v1/clients/{walkInId}", new { id = walkInId, fullName = "Baska Ad" });
        update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(update)).Should().Be("Clients.WalkInReadOnly");

        // Peşin müşterinin kimliği açıkça verilse de aynı kurallar işler (ödeme zorunlu).
        var explicitWalkIn = await PostAsync(client, ctx, Lines(ctx.ProductId, 1m), clientId: walkInId);
        explicitWalkIn.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(explicitWalkIn)).Should().Be("Sales.WalkInPaymentRequired");
    }

    // ---------- yardımcılar ----------

    private static object Cash() => new { method = "Cash" };

    private static object[] Lines(Guid productId, decimal quantity)
        => new object[] { new { productId, quantity } };

    private static DateOnly Today(int offsetDays) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offsetDays);

    private HttpClient ClientFor(SeedCtx ctx)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        return client;
    }

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        SeedCtx ctx,
        object[] lines,
        Guid? clientId = null,
        object? payment = null,
        Guid? requestId = null)
        => client.PostAsJsonAsync("/api/v1/sales", new
        {
            clinicId = ctx.ClinicId,
            clientId,
            requestId = requestId ?? Guid.NewGuid(),
            lines,
            payment,
        });

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<Guid> SaleIdAsync(HttpResponseMessage created)
    {
        created.IsSuccessStatusCode.Should().BeTrue();
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sale").GetProperty("id").GetGuid();
    }

    private static async Task<string?> GetSaleStatusAsync(HttpClient client, Guid saleId)
        => (await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales/{saleId}")).GetProperty("status").GetString();

    private static async Task<List<Guid>> ListIdsAsync(HttpClient client, SeedCtx ctx, string extraQuery)
    {
        var response = await client.GetAsync($"/api/v1/sales?clinicId={ctx.ClinicId:D}&pageSize=50{extraQuery}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ToList();
    }

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<decimal> GetStockAsync(SeedCtx ctx, Guid? productId = null)
        => WithDbAsync(db => db.ProductStocks.AsNoTracking()
            .Where(s => s.ClinicId == ctx.ClinicId && s.ProductId == (productId ?? ctx.ProductId))
            .Select(s => s.QuantityOnHand)
            .SingleOrDefaultAsync());

    private Task<int> SaleCountAsync(SeedCtx ctx)
        => WithDbAsync(db => db.Sales.AsNoTracking().CountAsync(s => s.TenantId == ctx.TenantId));

    private Task<int> WalkInClientCountAsync(SeedCtx ctx)
        => WithDbAsync(db => db.Clients.AsNoTracking().CountAsync(c => c.TenantId == ctx.TenantId && c.IsWalkIn));

    private Task<int> PaymentCountAsync(SeedCtx ctx)
        => WithDbAsync(db => db.Payments.IgnoreQueryFilters().AsNoTracking().CountAsync(p => p.TenantId == ctx.TenantId));

    private Task<List<Backend.Veteriner.Domain.Payments.Payment>> PaymentsAsync(SeedCtx ctx)
        => WithDbAsync(db => db.Payments.IgnoreQueryFilters().AsNoTracking().Where(p => p.TenantId == ctx.TenantId).ToListAsync());

    private Task<List<Backend.Veteriner.Domain.Charges.Charge>> ChargesAsync(SeedCtx ctx)
        => WithDbAsync(db => db.Charges.IgnoreQueryFilters().AsNoTracking().Where(c => c.TenantId == ctx.TenantId).ToListAsync());

    private Task<int> OutMovementCountAsync(SeedCtx ctx)
        => WithDbAsync(db => db.StockMovements.AsNoTracking()
            .CountAsync(m => m.TenantId == ctx.TenantId && m.MovementType == StockMovementType.Out));

    private Task<int> ReversalCountAsync(SeedCtx ctx)
        => WithDbAsync(db => db.StockMovements.AsNoTracking()
            .CountAsync(m => m.TenantId == ctx.TenantId && m.MovementType == StockMovementType.Reversal));

    private Task<List<StockMovement>> MovementsForAsync(Guid lineId)
        => WithDbAsync(db => db.StockMovements.AsNoTracking().Where(m => m.ReferenceId == lineId).ToListAsync());

    private Task<decimal> RemainingAsync(Guid batchId)
        => WithDbAsync(db => db.StockBatches.AsNoTracking().Where(b => b.Id == batchId).Select(b => b.RemainingQuantity).SingleAsync());

    private async Task DeactivateProductAsync(Guid productId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = await db.Products.SingleAsync(p => p.Id == productId);
        product.Deactivate(DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    /// <summary>Partiyi doğrudan yazar ve ürün stok satırını partilerin toplamına eşitler.</summary>
    private async Task<Guid> SeedBatchAsync(SeedCtx ctx, decimal quantity, string lot, DateOnly expiry)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = await db.Products.SingleAsync(p => p.Id == ctx.ProductId);
        var batch = StockBatch.Create(
            ctx.TenantId, ctx.ClinicId, product, quantity, new StockBatchDetails(LotNumber: lot, ExpiryDate: expiry), null, DateTime.UtcNow).Value!;
        db.StockBatches.Add(batch);

        var stock = await db.ProductStocks.SingleOrDefaultAsync(s => s.ClinicId == ctx.ClinicId && s.ProductId == ctx.ProductId);
        if (stock is null)
            db.ProductStocks.Add(new ProductStock(ctx.TenantId, ctx.ClinicId, ctx.ProductId, quantity, 0, DateTime.UtcNow));
        else
            stock.IncreaseQuantity(quantity, DateTime.UtcNow);

        await db.SaveChangesAsync();
        return batch.Id;
    }

    private async Task<HttpClient> SeedUserClientAsync(SeedCtx ctx, string[] permissions, bool includeClinicClaim = true)
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

    /// <param name="stock">Ürün 1 stoğu (null = stok satırı yok; partiler <see cref="SeedBatchAsync"/> ile eklenir). Ürün 2 hep 17 adet.</param>
    private async Task<SeedCtx> SeedAsync(decimal? stock, decimal minimum = 0m, string[]? permissions = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"sale-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Sa-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var product = new Product(tenant.Id, $"sp1-{Guid.NewGuid():N}"[..14], "Adet", 10.00m, "TRY", DateTime.UtcNow, sku: $"S1-{Guid.NewGuid():N}"[..14]);
        var product2 = new Product(tenant.Id, $"sp2-{Guid.NewGuid():N}"[..14], "Adet", 25.50m, "TRY", DateTime.UtcNow, sku: $"S2-{Guid.NewGuid():N}"[..14]);
        var vaccine = new Product(tenant.Id, $"sv-{Guid.NewGuid():N}"[..14], "Doz", 100m, "TRY", DateTime.UtcNow, sku: $"SV-{Guid.NewGuid():N}"[..14], kind: ProductKind.Vaccine);
        var controlled = new Product(
            tenant.Id, $"sc-{Guid.NewGuid():N}"[..14], "Adet", 50m, "TRY", DateTime.UtcNow, sku: $"SC-{Guid.NewGuid():N}"[..14],
            controlledClass: ControlledSubstanceClass.Narcotic);
        db.Products.AddRange(product, product2, vaccine, controlled);
        var customer = new Client(tenant.Id, $"SaleClient-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110099");
        db.Clients.Add(customer);
        await db.SaveChangesAsync();

        if (stock is { } onHand)
            db.ProductStocks.Add(new ProductStock(tenant.Id, clinic.Id, product.Id, onHand, minimum, DateTime.UtcNow));
        db.ProductStocks.Add(new ProductStock(tenant.Id, clinic.Id, product2.Id, 17m, 0, DateTime.UtcNow));
        db.ProductStocks.Add(new ProductStock(tenant.Id, clinic.Id, vaccine.Id, 10m, 0, DateTime.UtcNow));
        db.ProductStocks.Add(new ProductStock(tenant.Id, clinic.Id, controlled.Id, 10m, 0, DateTime.UtcNow));
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, permissions ?? AllPermissions, includeClinicClaimInJwt: true);

        return new SeedCtx(tenant.Id, clinic.Id, product.Id, product2.Id, vaccine.Id, controlled.Id, customer.Id, token);
    }

    private sealed record SeedCtx(
        Guid TenantId,
        Guid ClinicId,
        Guid ProductId,
        Guid Product2Id,
        Guid VaccineProductId,
        Guid ControlledProductId,
        Guid ClientId,
        string Token);
}
