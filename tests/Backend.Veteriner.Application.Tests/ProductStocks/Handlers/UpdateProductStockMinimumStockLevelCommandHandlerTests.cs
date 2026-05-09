using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductStocks.Commands.UpdateMinimumStockLevel;
using Backend.Veteriner.Application.ProductStocks.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.ProductStocks.Handlers;

public sealed class UpdateProductStockMinimumStockLevelCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IRepository<ProductStock>> _productStocksWrite = new();

    public UpdateProductStockMinimumStockLevelCommandHandlerTests()
    {
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _productStocksWrite
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private UpdateProductStockMinimumStockLevelCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _clinicsRead.Object,
            _productStocksWrite.Object);

    private static (Guid Tid, Guid ClinicId, Guid ProductId, Guid StockId, Product Product, ProductStock Stock, Clinic Clinic)
        SeedTrackedStock(decimal qty, decimal min)
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var stockId = Guid.NewGuid();

        var clinic = new Clinic(tid, "Klinik", "Ankara");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, clinicId);

        var product = new Product(tid, "Ürün X", "Adet", 5m, "TRY", sku: "SKU-X");
        typeof(Product).GetProperty(nameof(Product.Id))!.SetValue(product, productId);

        var stock = new ProductStock(tid, clinicId, productId, qty, min);
        typeof(ProductStock).GetProperty(nameof(ProductStock.Id))!.SetValue(stock, stockId);
        AttachProduct(stock, product);

        return (tid, clinicId, productId, stockId, product, stock, clinic);
    }

    private static void AttachProduct(ProductStock stock, Product product)
    {
        typeof(ProductStock).GetProperty(nameof(ProductStock.Product))!.SetValue(stock, product);
    }

    private void SetupReads(Guid tid, ProductStock stock, Clinic clinic)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
    }

    [Fact]
    public async Task Handle_Should_Update_Minimum_And_Preserve_QuantityOnHand()
    {
        var (_, _, _, stockId, _, stock, clinic) = SeedTrackedStock(qty: 10m, min: 2m);
        SetupReads(stock.TenantId, stock, clinic);

        var beforeQty = stock.QuantityOnHand;
        var result = await CreateHandler().Handle(
            new UpdateProductStockMinimumStockLevelCommand(stockId, 77m),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MinimumStockLevel.Should().Be(77m);
        stock.QuantityOnHand.Should().Be(beforeQty);
        stock.MinimumStockLevel.Should().Be(77m);
        _productStocksWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_TenantMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var (_, _, _, stockId, _, _, _) = SeedTrackedStock(1m, 0m);

        var result = await CreateHandler().Handle(
            new UpdateProductStockMinimumStockLevelCommand(stockId, 1m),
            CancellationToken.None);

        result.Error!.Code.Should().Be("Tenants.ContextMissing");
        _productStocksWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Stock_NotFound()
    {
        var tid = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductStock?)null);

        var result = await CreateHandler().Handle(
            new UpdateProductStockMinimumStockLevelCommand(stockId, 3m),
            CancellationToken.None);

        result.Error!.Code.Should().Be("ProductStocks.NotFound");
        _productStocksWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Clinic_Context_Mismatch()
    {
        var (_, clinicId, _, stockId, _, stock, clinic) = SeedTrackedStock(5m, 1m);
        SetupReads(stock.TenantId, stock, clinic);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(
            new UpdateProductStockMinimumStockLevelCommand(stockId, 8m),
            CancellationToken.None);

        result.Error!.Code.Should().Be("ProductStocks.ClinicContextMismatch");
        _productStocksWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Propagate_AccessDenied_From_ScopeResolver()
    {
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(Array.Empty<Guid>());
        var handler = new UpdateProductStockMinimumStockLevelCommandHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            scope.Object,
            _clinicsRead.Object,
            _productStocksWrite.Object);

        var (_, _, _, stockId, _, stock, clinic) = SeedTrackedStock(3m, 1m);
        SetupReads(stock.TenantId, stock, clinic);

        var result = await handler.Handle(
            new UpdateProductStockMinimumStockLevelCommand(stockId, 4m),
            CancellationToken.None);

        result.Error!.Code.Should().Be("Clinics.AccessDenied");
        _productStocksWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Succeed_When_ClinicAdmin_Assigned_To_Stock_Clinic()
    {
        var (_, clinicId, _, stockId, _, stock, clinic) = SeedTrackedStock(9m, 1m);
        _tenantContext.SetupGet(t => t.TenantId).Returns(stock.TenantId);
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);

        var resolver = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { clinicId });
        var handler = new UpdateProductStockMinimumStockLevelCommandHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver.Object,
            _clinicsRead.Object,
            _productStocksWrite.Object);

        var result = await handler.Handle(
            new UpdateProductStockMinimumStockLevelCommand(stockId, 22m),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MinimumStockLevel.Should().Be(22m);
        stock.MinimumStockLevel.Should().Be(22m);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ClinicRow_Missing()
    {
        var (_, _, _, stockId, _, stock, _) = SeedTrackedStock(2m, 0m);
        _tenantContext.SetupGet(t => t.TenantId).Returns(stock.TenantId);
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateHandler().Handle(
            new UpdateProductStockMinimumStockLevelCommand(stockId, 3m),
            CancellationToken.None);

        result.Error!.Code.Should().Be("Clinics.NotFound");
        _productStocksWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
