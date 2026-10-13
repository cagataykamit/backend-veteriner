using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.StockBatches;
using Backend.Veteriner.Application.StockMovements.Commands.Reverse;
using Backend.Veteriner.Application.StockMovements.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.StockMovements.Handlers;

/// <summary>ADR-015: ters hareket orijinalin partisine uygulanır (Out → aynı partiye iade, In → parti kalanından düşer).</summary>
public sealed class ReverseStockMovementBatchTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IReadRepository<Product>> _productsRead = new();
    private readonly Mock<IReadRepository<ProductCategory>> _categoriesRead = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IRepository<ProductStock>> _productStocksWrite = new();
    private readonly Mock<IRepository<StockMovement>> _stockMovementsWrite = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();
    private readonly Product _product;
    private StockMovement? _written;

    public ReverseStockMovementBatchTests()
    {
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _clientContext.SetupGet(c => c.UserId).Returns(Guid.NewGuid());
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);

        var clinic = new Clinic(_tenantId, "Klinik", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, _clinicId);
        _product = new Product(_tenantId, "Ürün", "Doz", 10m, "TRY", sku: "SKU-1");
        typeof(Product).GetProperty(nameof(Product.Id))!.SetValue(_product, _productId);

        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        _productsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_product);
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .Callback((StockMovement m, CancellationToken _) => _written = m)
            .ReturnsAsync((StockMovement e, CancellationToken _) => e);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private ReverseStockMovementCommandHandler CreateHandler(params StockBatch[] batches)
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
            new StockBatchResolver(StockBatchMocks.WithBatches(batches).Object),
            _uow.Object);

    private StockBatch Batch(decimal qty)
        => StockBatchMocks.Batch(_tenantId, _clinicId, _product, qty, "L", new DateOnly(2027, 1, 1));

    private StockMovement Movement(StockMovementType type, decimal qty, StockBatch? batch = null)
    {
        var m = new StockMovement(_tenantId, _clinicId, _productId, type, qty, DateTime.UtcNow);
        if (batch is not null)
            m.LinkBatch(batch);
        _stockMovementsWrite
            .Setup(r => r.FirstOrDefaultAsync(
                It.Is<ISpecification<StockMovement>>(s => s is StockMovementByIdSpec), It.IsAny<CancellationToken>()))
            .ReturnsAsync(m);
        return m;
    }

    private ProductStock SetupStock(decimal qty)
    {
        var stock = new ProductStock(_tenantId, _clinicId, _productId, qty, 0);
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        return stock;
    }

    private static ReverseStockMovementCommand Cmd(Guid id) => new(id, "Yanlış parti", null, null);

    [Fact]
    public async Task Reversing_Out_Should_Return_Quantity_To_The_Same_Batch()
    {
        var batch = Batch(10m);
        batch.Decrease(4m); // Out 4 sonrası: 6
        var stock = SetupStock(6m);
        var original = Movement(StockMovementType.Out, 4m, batch);

        var result = await CreateHandler(batch).Handle(Cmd(original.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        batch.RemainingQuantity.Should().Be(10m);
        stock.QuantityOnHand.Should().Be(10m);
        _written!.MovementType.Should().Be(StockMovementType.Reversal);
        _written.BatchId.Should().Be(batch.Id);
        _written.BatchBalanceAfter.Should().Be(10m);
        result.Value!.Movement.BatchId.Should().Be(batch.Id);
    }

    [Fact]
    public async Task Reversing_Batched_In_Should_Decrease_Batch_And_Stock()
    {
        var batch = Batch(8m);
        var stock = SetupStock(8m);
        var original = Movement(StockMovementType.In, 8m, batch);

        var result = await CreateHandler(batch).Handle(Cmd(original.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        batch.RemainingQuantity.Should().Be(0m);
        stock.QuantityOnHand.Should().Be(0m);
        _written!.BatchBalanceAfter.Should().Be(0m);
    }

    [Fact]
    public async Task Reversing_Batched_In_Should_Fail_InsufficientStock_When_Batch_Was_Partly_Used_Even_If_Stock_Is_Enough()
    {
        var batch = Batch(8m);
        batch.Decrease(5m); // parti kısmen kullanıldı: 3
        var stock = SetupStock(20m); // başka stok var ama bu parti yetmiyor
        var original = Movement(StockMovementType.In, 8m, batch);

        var result = await CreateHandler(batch).Handle(Cmd(original.Id), CancellationToken.None);

        result.Error.Code.Should().Be("StockMovements.InsufficientStock");
        batch.RemainingQuantity.Should().Be(3m);
        stock.QuantityOnHand.Should().Be(20m);
        _stockMovementsWrite.Verify(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reversing_Unbatched_In_Should_Require_Enough_Unbatched_Balance()
    {
        var batch = Batch(7m);
        var stock = SetupStock(10m); // 7 partili + 3 partisiz
        var original = Movement(StockMovementType.In, 5m); // partisiz giriş 5, ama partisiz bakiye 3

        var result = await CreateHandler(batch).Handle(Cmd(original.Id), CancellationToken.None);

        result.Error.Code.Should().Be("StockMovements.InsufficientStock");
        stock.QuantityOnHand.Should().Be(10m);
        batch.RemainingQuantity.Should().Be(7m);
    }

    [Fact]
    public async Task Reversing_Unbatched_Movements_Should_Behave_As_Before_And_Not_Link_A_Batch()
    {
        var stock = SetupStock(6m);
        var original = Movement(StockMovementType.Out, 4m); // eski/partisiz çıkış

        var result = await CreateHandler().Handle(Cmd(original.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stock.QuantityOnHand.Should().Be(10m);
        _written!.BatchId.Should().BeNull();
        _written.BatchBalanceAfter.Should().BeNull();
    }

    [Fact]
    public async Task Reversing_Should_Fail_BatchNotFound_When_Linked_Batch_Is_Missing()
    {
        SetupStock(6m);
        var batch = Batch(6m);
        var original = Movement(StockMovementType.Out, 4m, batch);

        var result = await CreateHandler().Handle(Cmd(original.Id), CancellationToken.None);

        result.Error.Code.Should().Be("StockBatches.NotFound");
    }
}
