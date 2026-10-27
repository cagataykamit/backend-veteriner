using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.StockBatches;
using Backend.Veteriner.Application.StockMovements.Commands.Create;
using Backend.Veteriner.Application.StockMovements.Commands.Create.Validators;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.StockMovements.Handlers;

/// <summary>ADR-015: giriş parti açar, çıkış FEFO/açık partiden düşer, sayım parti/partisiz bakiyeye uygulanır.</summary>
public sealed class CreateStockMovementBatchTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IReadRepository<Product>> _productsRead = new();
    private readonly Mock<IReadRepository<ProductCategory>> _categoriesRead = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IRepository<ProductStock>> _productStocksWrite = new();
    private readonly Mock<IRepository<StockMovement>> _stockMovementsWrite = new();
    private readonly Mock<IRepository<StockBatch>> _batchesWrite = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();
    private readonly Clinic _clinic;
    private StockMovement? _written;

    public CreateStockMovementBatchTests()
    {
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _clientContext.SetupGet(c => c.UserId).Returns((Guid?)null);
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);

        _clinic = new Clinic(_tenantId, "Klinik", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(_clinic, _clinicId);
        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_clinic);
        _productStocksWrite
            .Setup(r => r.AddAsync(It.IsAny<ProductStock>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductStock e, CancellationToken _) => e);
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .Callback((StockMovement m, CancellationToken _) => _written = m)
            .ReturnsAsync((StockMovement e, CancellationToken _) => e);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private Product SetupProduct(ProductKind kind = ProductKind.Other, ControlledSubstanceClass controlled = ControlledSubstanceClass.None)
    {
        var product = new Product(_tenantId, "Ürün", "Doz", 10m, "TRY", DateTime.UtcNow, sku: "SKU-1", kind: kind, controlledClass: controlled);
        typeof(Product).GetProperty(nameof(Product.Id))!.SetValue(product, _productId);
        _productsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        return product;
    }

    private StockBatch Batch(Product product, decimal qty, DateOnly? expiry = null, string? lot = "L")
        => StockBatchMocks.Batch(_tenantId, _clinicId, product, qty, lot, expiry);

    private CreateStockMovementCommandHandler CreateHandler(params StockBatch[] existingBatches)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            ClinicReadScopeResolverMock.Default().Object,
            _productsRead.Object,
            _categoriesRead.Object,
            _clinicsRead.Object,
            _productStocksWrite.Object,
            _stockMovementsWrite.Object,
            _batchesWrite.Object,
            new StockBatchResolver(StockBatchMocks.WithBatches(existingBatches).Object),
            _uow.Object,
            StockBatchMocks.Clock);

    private ProductStock SetupStock(decimal qty)
    {
        var stock = new ProductStock(_tenantId, _clinicId, _productId, qty, 0, DateTime.UtcNow);
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        return stock;
    }

    private CreateStockMovementCommand Cmd(
        StockMovementType type,
        decimal qty,
        StockBatchDetails? batch = null,
        Guid? batchId = null,
        DateOnly? notExpiredOn = null)
        => new(
            _clinicId, _productId, type, qty,
            UnitCost: null, Reason: null, ReferenceType: null, ReferenceId: null, OccurredAtUtc: null, Notes: null,
            Batch: batch, BatchId: batchId, NotExpiredOn: notExpiredOn);

    private static readonly DateOnly Today = new(2026, 10, 13);

    // ---- Giriş ----

    [Fact]
    public async Task In_With_Batch_Should_Create_Batch_Link_Movement_And_Increase_Stock()
    {
        SetupProduct();
        var stock = SetupStock(5m);
        var details = new StockBatchDetails(LotNumber: "A1", ExpiryDate: new DateOnly(2027, 1, 1), SupplierName: "Firma", InvoiceNumber: "F1");

        var result = await CreateHandler().Handle(Cmd(StockMovementType.In, 4m, details), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stock.QuantityOnHand.Should().Be(9m);
        _written!.Batch.Should().NotBeNull();
        _written.Batch!.LotNumber.Should().Be("A1");
        _written.Batch.InitialQuantity.Should().Be(4m);
        _written.Batch.RemainingQuantity.Should().Be(4m);
        _written.Batch.SupplierName.Should().Be("Firma");
        _written.BatchId.Should().Be(_written.Batch.Id);
        _written.BatchBalanceAfter.Should().Be(4m);
        result.Value!.Movement.BatchId.Should().Be(_written.Batch.Id);
        result.Value.Movement.BatchBalanceAfter.Should().Be(4m);
    }

    [Fact]
    public async Task Initial_With_Batch_Should_Create_Stock_Row_And_Batch()
    {
        SetupProduct();
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductStock?)null);

        var result = await CreateHandler().Handle(
            Cmd(StockMovementType.Initial, 6m, new StockBatchDetails(LotNumber: "I1")), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _written!.Batch!.InitialQuantity.Should().Be(6m);
        _productStocksWrite.Verify(r => r.AddAsync(
            It.Is<ProductStock>(s => s.QuantityOnHand == 6m), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task In_Without_Batch_Should_Go_To_Unbatched_Balance_For_Plain_Product()
    {
        SetupProduct();
        var stock = SetupStock(5m);

        var result = await CreateHandler().Handle(Cmd(StockMovementType.In, 4m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stock.QuantityOnHand.Should().Be(9m);
        _written!.BatchId.Should().BeNull();
        _written.Batch.Should().BeNull();
        _written.BatchBalanceAfter.Should().BeNull();
    }

    [Fact]
    public async Task In_With_Empty_Batch_Object_Should_Be_Treated_As_No_Batch()
    {
        SetupProduct();
        SetupStock(5m);

        var result = await CreateHandler().Handle(
            Cmd(StockMovementType.In, 1m, new StockBatchDetails(LotNumber: "  ")), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _written!.Batch.Should().BeNull();
    }

    [Theory]
    [InlineData(ProductKind.Vaccine, ControlledSubstanceClass.None)]
    [InlineData(ProductKind.Medicine, ControlledSubstanceClass.Narcotic)]
    [InlineData(ProductKind.Other, ControlledSubstanceClass.Psychotropic)]
    public async Task In_Should_Require_Batch_With_Lot_And_Expiry_For_Vaccine_And_Controlled(
        ProductKind kind, ControlledSubstanceClass controlled)
    {
        SetupProduct(kind, controlled);
        var stock = SetupStock(5m);

        var withoutBatch = await CreateHandler().Handle(Cmd(StockMovementType.In, 1m), CancellationToken.None);
        var withoutExpiry = await CreateHandler().Handle(
            Cmd(StockMovementType.In, 1m, new StockBatchDetails(LotNumber: "L")), CancellationToken.None);

        withoutBatch.Error.Code.Should().Be(StockBatch.LotAndExpiryRequiredCode);
        withoutExpiry.Error.Code.Should().Be(StockBatch.LotAndExpiryRequiredCode);
        stock.QuantityOnHand.Should().Be(5m);
        _stockMovementsWrite.Verify(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task In_Should_Accept_Vaccine_With_Lot_And_Expiry()
    {
        SetupProduct(ProductKind.Vaccine);
        SetupStock(5m);

        var result = await CreateHandler().Handle(
            Cmd(StockMovementType.In, 10m, new StockBatchDetails(LotNumber: "V1", ExpiryDate: new DateOnly(2027, 3, 1))),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _written!.Batch!.ExpiryDate.Should().Be(new DateOnly(2027, 3, 1));
    }

    // ---- Çıkış ----

    [Fact]
    public async Task Out_With_Explicit_Batch_Should_Decrease_Batch_And_Stock_And_Record_Balance()
    {
        var product = SetupProduct();
        var stock = SetupStock(10m);
        var other = Batch(product, 4m, new DateOnly(2027, 1, 1));
        var chosen = Batch(product, 6m, new DateOnly(2027, 6, 1));

        var result = await CreateHandler(other, chosen).Handle(
            Cmd(StockMovementType.Out, 2m, batchId: chosen.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        chosen.RemainingQuantity.Should().Be(4m);
        other.RemainingQuantity.Should().Be(4m);
        stock.QuantityOnHand.Should().Be(8m);
        _written!.BatchId.Should().Be(chosen.Id);
        _written.BatchBalanceAfter.Should().Be(4m);
    }

    [Fact]
    public async Task Out_Without_Batch_Should_Use_Fefo_Earliest_Sufficient_Batch()
    {
        var product = SetupProduct();
        SetupStock(20m);
        var late = Batch(product, 10m, new DateOnly(2028, 1, 1));
        var soon = Batch(product, 10m, new DateOnly(2027, 1, 1));

        var result = await CreateHandler(late, soon).Handle(Cmd(StockMovementType.Out, 3m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        soon.RemainingQuantity.Should().Be(7m);
        late.RemainingQuantity.Should().Be(10m);
        _written!.BatchId.Should().Be(soon.Id);
    }

    [Fact]
    public async Task Out_Manual_Should_Be_Allowed_From_Expired_Batch_For_Disposal()
    {
        var product = SetupProduct();
        SetupStock(5m);
        var expired = Batch(product, 5m, new DateOnly(2026, 1, 1));

        var result = await CreateHandler(expired).Handle(
            Cmd(StockMovementType.Out, 5m, batchId: expired.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        expired.RemainingQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task Out_Should_Fall_Back_To_Unbatched_Balance_When_No_Batch_Covers_Quantity()
    {
        var product = SetupProduct();
        var stock = SetupStock(10m); // 4 partili + 6 partisiz
        var batch = Batch(product, 4m, new DateOnly(2027, 1, 1));

        var result = await CreateHandler(batch).Handle(Cmd(StockMovementType.Out, 5m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        batch.RemainingQuantity.Should().Be(4m);
        stock.QuantityOnHand.Should().Be(5m);
        _written!.BatchId.Should().BeNull();
    }

    [Fact]
    public async Task Out_Should_Fail_InsufficientStock_When_No_Single_Batch_Or_Unbatched_Covers_It()
    {
        var product = SetupProduct();
        var stock = SetupStock(10m); // 4 + 6 partili, partisiz 0
        var a = Batch(product, 4m, new DateOnly(2027, 1, 1));
        var b = Batch(product, 6m, new DateOnly(2027, 2, 1));

        var result = await CreateHandler(a, b).Handle(Cmd(StockMovementType.Out, 7m), CancellationToken.None);

        result.Error.Code.Should().Be("StockMovements.InsufficientStock");
        stock.QuantityOnHand.Should().Be(10m);
        a.RemainingQuantity.Should().Be(4m);
        b.RemainingQuantity.Should().Be(6m);
        _stockMovementsWrite.Verify(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Out_With_Explicit_Batch_Should_Fail_InsufficientStock_When_Batch_Too_Small_Even_If_Stock_Is_Enough()
    {
        var product = SetupProduct();
        var stock = SetupStock(10m);
        var small = Batch(product, 2m);

        var result = await CreateHandler(small).Handle(
            Cmd(StockMovementType.Out, 3m, batchId: small.Id), CancellationToken.None);

        result.Error.Code.Should().Be("StockMovements.InsufficientStock");
        stock.QuantityOnHand.Should().Be(10m);
        small.RemainingQuantity.Should().Be(2m);
    }

    [Fact]
    public async Task Out_Should_Fail_BatchNotFound_When_Batch_Belongs_To_Another_Product_Or_Clinic()
    {
        var product = SetupProduct();
        SetupStock(10m);
        var foreign = StockBatchMocks.Batch(_tenantId, Guid.NewGuid(), product, 5m, "X");

        var result = await CreateHandler(foreign).Handle(
            Cmd(StockMovementType.Out, 1m, batchId: foreign.Id), CancellationToken.None);

        result.Error.Code.Should().Be("StockBatches.NotFound");
    }

    [Fact]
    public async Task Out_Application_Path_Should_Reject_Expired_Batch_And_Skip_Expired_In_Default_Selection()
    {
        var product = SetupProduct();
        SetupStock(20m);
        var expired = Batch(product, 10m, new DateOnly(2026, 1, 1));
        var valid = Batch(product, 10m, new DateOnly(2027, 1, 1));

        var explicitExpired = await CreateHandler(expired, valid).Handle(
            Cmd(StockMovementType.Out, 1m, batchId: expired.Id, notExpiredOn: Today), CancellationToken.None);
        var defaulted = await CreateHandler(expired, valid).Handle(
            Cmd(StockMovementType.Out, 1m, notExpiredOn: Today), CancellationToken.None);

        explicitExpired.Error.Code.Should().Be(StockBatchResolver.ExpiredCode);
        defaulted.IsSuccess.Should().BeTrue();
        valid.RemainingQuantity.Should().Be(9m);
        expired.RemainingQuantity.Should().Be(10m);
    }

    [Fact]
    public async Task Out_Application_Path_Should_Fail_Expired_When_Only_Expired_Batches_Cover_Quantity()
    {
        var product = SetupProduct();
        SetupStock(10m);
        var expired = Batch(product, 10m, new DateOnly(2026, 1, 1));

        var result = await CreateHandler(expired).Handle(
            Cmd(StockMovementType.Out, 2m, notExpiredOn: Today), CancellationToken.None);

        result.Error.Code.Should().Be(StockBatchResolver.ExpiredCode);
        expired.RemainingQuantity.Should().Be(10m);
    }

    [Fact]
    public async Task Out_Application_Path_Should_Report_Available_And_Expired_When_Balance_Is_Short()
    {
        var product = SetupProduct();
        var stock = SetupStock(8m); // 5 uygun + 3 süresi geçmiş
        var valid = Batch(product, 5m, new DateOnly(2027, 12, 31));
        var expired = Batch(product, 3m, new DateOnly(2026, 1, 1));

        var shortBySingleBatch = await CreateHandler(valid, expired).Handle(
            Cmd(StockMovementType.Out, 6m, notExpiredOn: Today), CancellationToken.None);
        var shortByTotal = await CreateHandler(valid, expired).Handle(
            Cmd(StockMovementType.Out, 9m, notExpiredOn: Today), CancellationToken.None);

        shortBySingleBatch.Error.Code.Should().Be("StockMovements.InsufficientStock");
        shortBySingleBatch.Error.Message.Should().Be("Yetersiz bakiye: kullanılabilir 5, süresi geçmiş 3 (hariç)");
        shortByTotal.Error.Message.Should().Be("Yetersiz bakiye: kullanılabilir 5, süresi geçmiş 3 (hariç)");
        stock.QuantityOnHand.Should().Be(8m);
        valid.RemainingQuantity.Should().Be(5m);
    }

    [Fact]
    public async Task Out_Application_Path_Should_Say_Split_When_Available_Total_Covers_But_No_Single_Batch()
    {
        var product = SetupProduct();
        SetupStock(6m);
        var a = Batch(product, 3m, new DateOnly(2027, 1, 1));
        var b = Batch(product, 3m, new DateOnly(2027, 2, 1));

        var result = await CreateHandler(a, b).Handle(
            Cmd(StockMovementType.Out, 4m, notExpiredOn: Today), CancellationToken.None);

        result.Error.Code.Should().Be("StockMovements.InsufficientStock");
        result.Error.Message.Should().Contain("tek başına");
    }

    // ---- Süresi geçmiş giriş onayı ----

    [Fact]
    public async Task In_With_Past_Expiry_Should_Require_Acknowledgement_And_Write_Nothing()
    {
        SetupProduct();
        var stock = SetupStock(5m);
        var details = new StockBatchDetails(LotNumber: "ESKI", ExpiryDate: new DateOnly(2026, 1, 1));

        var result = await CreateHandler().Handle(Cmd(StockMovementType.In, 3m, details), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(StockBatch.ExpiredAcknowledgementRequiredCode);
        result.Error.Message.Should().Be("Bu partinin son kullanma tarihi geçmiş. Devam etmek için onaylayın.");
        stock.QuantityOnHand.Should().Be(5m);
        _stockMovementsWrite.Verify(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(StockMovementType.In)]
    [InlineData(StockMovementType.Initial)]
    public async Task Entry_With_Past_Expiry_And_Acknowledgement_Should_Be_Accepted(StockMovementType type)
    {
        SetupProduct();
        if (type == StockMovementType.Initial)
        {
            _productStocksWrite
                .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ProductStock?)null);
        }
        else
        {
            SetupStock(5m);
        }

        var details = new StockBatchDetails(LotNumber: "ESKI", ExpiryDate: new DateOnly(2026, 1, 1));

        var result = await CreateHandler().Handle(
            Cmd(type, 3m, details) with { AcknowledgeExpired = true }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _written!.Batch!.ExpiryDate.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public async Task In_With_Expiry_Today_Should_Not_Require_Acknowledgement()
    {
        SetupProduct();
        SetupStock(5m);

        var result = await CreateHandler().Handle(
            Cmd(StockMovementType.In, 1m, new StockBatchDetails(LotNumber: "BUGUN", ExpiryDate: Today)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // ---- Sayım ----

    [Fact]
    public async Task Adjustment_With_Batch_Should_Set_Batch_Remaining_And_Shift_Stock_By_Delta()
    {
        var product = SetupProduct();
        var stock = SetupStock(10m);
        var batch = Batch(product, 6m);

        var result = await CreateHandler(batch).Handle(
            Cmd(StockMovementType.Adjustment, 4m, batchId: batch.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        batch.RemainingQuantity.Should().Be(4m);
        stock.QuantityOnHand.Should().Be(8m);
        _written!.Quantity.Should().Be(2m);
        _written.BatchId.Should().Be(batch.Id);
        _written.BatchBalanceAfter.Should().Be(4m);
    }

    [Fact]
    public async Task Adjustment_With_Batch_Should_Fail_Unchanged_When_Target_Equals_Remaining()
    {
        var product = SetupProduct();
        SetupStock(10m);
        var batch = Batch(product, 6m);

        var result = await CreateHandler(batch).Handle(
            Cmd(StockMovementType.Adjustment, 6m, batchId: batch.Id), CancellationToken.None);

        result.Error.Code.Should().Be("StockMovements.AdjustmentUnchanged");
    }

    [Fact]
    public async Task Adjustment_Without_Batch_Should_Apply_Delta_To_Unbatched_Balance()
    {
        var product = SetupProduct();
        var stock = SetupStock(10m); // 6 partili + 4 partisiz
        var batch = Batch(product, 6m);

        var up = await CreateHandler(batch).Handle(Cmd(StockMovementType.Adjustment, 12m), CancellationToken.None);

        up.IsSuccess.Should().BeTrue();
        stock.QuantityOnHand.Should().Be(12m);
        batch.RemainingQuantity.Should().Be(6m);
        _written!.BatchId.Should().BeNull();
    }

    [Fact]
    public async Task Adjustment_Without_Batch_Should_Require_Batch_When_Decrease_Exceeds_Unbatched_Balance()
    {
        var product = SetupProduct();
        var stock = SetupStock(10m); // 6 partili + 4 partisiz
        var batch = Batch(product, 6m);

        var ok = await CreateHandler(batch).Handle(Cmd(StockMovementType.Adjustment, 6m), CancellationToken.None); // -4 = tam partisiz
        stock = SetupStock(10m);
        var tooMuch = await CreateHandler(batch).Handle(Cmd(StockMovementType.Adjustment, 5m), CancellationToken.None); // -5 > 4

        ok.IsSuccess.Should().BeTrue();
        tooMuch.Error.Code.Should().Be("StockMovements.BatchRequired");
        stock.QuantityOnHand.Should().Be(10m);
        batch.RemainingQuantity.Should().Be(6m);
    }

    // ---- Doğrulayıcı ----

    [Fact]
    public void Validator_Should_Reject_BatchId_On_Entry_And_Batch_On_Exit_And_Long_Fields()
    {
        var v = new CreateStockMovementCommandValidator();

        v.Validate(Cmd(StockMovementType.In, 1m, batchId: Guid.NewGuid())).IsValid.Should().BeFalse();
        v.Validate(Cmd(StockMovementType.Initial, 1m, batchId: Guid.NewGuid())).IsValid.Should().BeFalse();
        v.Validate(Cmd(StockMovementType.Out, 1m, new StockBatchDetails(LotNumber: "L"))).IsValid.Should().BeFalse();
        v.Validate(Cmd(StockMovementType.Adjustment, 1m, new StockBatchDetails(LotNumber: "L"))).IsValid.Should().BeFalse();
        v.Validate(Cmd(StockMovementType.In, 1m, new StockBatchDetails(LotNumber: new string('x', 101)))).IsValid.Should().BeFalse();
        v.Validate(Cmd(StockMovementType.In, 1m, new StockBatchDetails(LotNumber: new string('x', 100)))).IsValid.Should().BeTrue();
        v.Validate(Cmd(StockMovementType.Out, 1m, batchId: Guid.NewGuid())).IsValid.Should().BeTrue();
    }
}
