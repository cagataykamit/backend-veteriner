using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.StockMovements.Commands.Create;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.StockMovements.Handlers;

public sealed class CreateStockMovementCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Product>> _productsRead = new();
    private readonly Mock<IReadRepository<ProductCategory>> _categoriesRead = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IRepository<ProductStock>> _productStocksWrite = new();
    private readonly Mock<IRepository<StockMovement>> _stockMovementsWrite = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public CreateStockMovementCommandHandlerTests()
    {
        _clientContext.SetupGet(c => c.UserId).Returns((Guid?)null);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _categoriesRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductCategory>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductCategory?)null);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private CreateStockMovementCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _scopeResolver.Object,
            _productsRead.Object,
            _categoriesRead.Object,
            _clinicsRead.Object,
            _productStocksWrite.Object,
            _stockMovementsWrite.Object,
            _uow.Object);

    private static (Guid TenantId, Guid ClinicId, Guid ProductId, Clinic Clinic, Product Product) SeedIdsAndEntities()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var clinic = new Clinic(tenantId, "Klinik", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, clinicId);
        var product = new Product(tenantId, "Ürün", "Adet", 10m, "TRY", sku: "SKU-1");
        typeof(Product).GetProperty(nameof(Product.Id))!.SetValue(product, productId);
        return (tenantId, clinicId, productId, clinic, product);
    }

    private void SetupDefaultReads(Guid tenantId, Guid clinicId, Product product, Clinic clinic)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        _productsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
    }

    private static CreateStockMovementCommand Cmd(
        Guid clinicId,
        Guid productId,
        StockMovementType type,
        decimal qty)
        => new(
            clinicId,
            productId,
            type,
            qty,
            UnitCost: null,
            Reason: null,
            ReferenceType: null,
            ReferenceId: null,
            OccurredAtUtc: null,
            Notes: null);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var (_, clinicId, productId, _, _) = SeedIdsAndEntities();

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.In, 1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("Tenants.ContextMissing");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ActiveClinicContext_Mismatch()
    {
        var (tenantId, clinicId, productId, clinic, product) = SeedIdsAndEntities();
        SetupDefaultReads(tenantId, clinicId, product, clinic);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.In, 1), CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.ClinicContextMismatch");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ClinicScope_AccessDenied()
    {
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(Array.Empty<Guid>());
        var handler = new CreateStockMovementCommandHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            scope.Object,
            _productsRead.Object,
            _categoriesRead.Object,
            _clinicsRead.Object,
            _productStocksWrite.Object,
            _stockMovementsWrite.Object,
            _uow.Object);

        var (tenantId, clinicId, productId, clinic, product) = SeedIdsAndEntities();
        SetupDefaultReads(tenantId, clinicId, product, clinic);

        var result = await handler.Handle(Cmd(clinicId, productId, StockMovementType.In, 1), CancellationToken.None);

        result.Error!.Code.Should().Be("Clinics.AccessDenied");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Clinic_NotFound()
    {
        var (tenantId, clinicId, productId, _, product) = SeedIdsAndEntities();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);
        _productsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.In, 1), CancellationToken.None);

        result.Error!.Code.Should().Be("Clinics.NotFound");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Product_NotFound()
    {
        var (tenantId, clinicId, productId, clinic, _) = SeedIdsAndEntities();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        _productsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.In, 1), CancellationToken.None);

        result.Error!.Code.Should().Be("Products.NotFound");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Product_Inactive()
    {
        var (tenantId, clinicId, productId, clinic, product) = SeedIdsAndEntities();
        product.Deactivate();
        SetupDefaultReads(tenantId, clinicId, product, clinic);
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductStock?)null);

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.In, 1), CancellationToken.None);

        result.Error!.Code.Should().Be("Products.Inactive");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Initial_Should_CreateStock_And_Movement_When_NoRow()
    {
        var (tenantId, clinicId, productId, clinic, product) = SeedIdsAndEntities();
        SetupDefaultReads(tenantId, clinicId, product, clinic);
        ProductStock? addedStock = null;
        StockMovement? addedMovement = null;
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductStock?)null);
        _productStocksWrite
            .Setup(r => r.AddAsync(It.IsAny<ProductStock>(), It.IsAny<CancellationToken>()))
            .Callback<ProductStock, CancellationToken>((s, _) => addedStock = s)
            .ReturnsAsync((ProductStock e, CancellationToken _) => e);
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .Callback<StockMovement, CancellationToken>((m, _) => addedMovement = m)
            .ReturnsAsync((StockMovement e, CancellationToken _) => e);

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.Initial, 7), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        addedStock.Should().NotBeNull();
        addedStock!.QuantityOnHand.Should().Be(7);
        addedMovement.Should().NotBeNull();
        addedMovement!.MovementType.Should().Be(StockMovementType.Initial);
        addedMovement.Quantity.Should().Be(7);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Initial_Should_Fail_When_Stock_AlreadyExists()
    {
        var (tenantId, clinicId, productId, clinic, product) = SeedIdsAndEntities();
        SetupDefaultReads(tenantId, clinicId, product, clinic);
        var existing = new ProductStock(tenantId, clinicId, productId, 1m, 0);
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.Initial, 3), CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.StockAlreadyInitialized");
        _stockMovementsWrite.Verify(
            r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_In_Should_Create_ProductStock_When_Missing()
    {
        var (tenantId, clinicId, productId, clinic, product) = SeedIdsAndEntities();
        SetupDefaultReads(tenantId, clinicId, product, clinic);
        ProductStock? addedStock = null;
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductStock?)null);
        _productStocksWrite
            .Setup(r => r.AddAsync(It.IsAny<ProductStock>(), It.IsAny<CancellationToken>()))
            .Callback<ProductStock, CancellationToken>((s, _) => addedStock = s)
            .ReturnsAsync((ProductStock e, CancellationToken _) => e);
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMovement e, CancellationToken _) => e);

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.In, 4), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        addedStock!.QuantityOnHand.Should().Be(4);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_In_Should_Increase_Existing_Stock()
    {
        var (tenantId, clinicId, productId, clinic, product) = SeedIdsAndEntities();
        SetupDefaultReads(tenantId, clinicId, product, clinic);
        var stock = new ProductStock(tenantId, clinicId, productId, 10m, 0);
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMovement e, CancellationToken _) => e);

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.In, 3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stock.QuantityOnHand.Should().Be(13);
        _productStocksWrite.Verify(r => r.AddAsync(It.IsAny<ProductStock>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Out_Should_Decrease_When_Sufficient()
    {
        var (tenantId, clinicId, productId, clinic, product) = SeedIdsAndEntities();
        SetupDefaultReads(tenantId, clinicId, product, clinic);
        var stock = new ProductStock(tenantId, clinicId, productId, 10m, 0);
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMovement e, CancellationToken _) => e);

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.Out, 4), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stock.QuantityOnHand.Should().Be(6);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Out_Should_Fail_When_No_ProductStock_Row()
    {
        var (tenantId, clinicId, productId, clinic, product) = SeedIdsAndEntities();
        SetupDefaultReads(tenantId, clinicId, product, clinic);
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductStock?)null);

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.Out, 1), CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.InsufficientStock");
        _productStocksWrite.Verify(r => r.AddAsync(It.IsAny<ProductStock>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Out_Should_Fail_When_Insufficient_OnHand()
    {
        var (tenantId, clinicId, productId, clinic, product) = SeedIdsAndEntities();
        SetupDefaultReads(tenantId, clinicId, product, clinic);
        var stock = new ProductStock(tenantId, clinicId, productId, 2m, 0);
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.Out, 5), CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.InsufficientStock");
        stock.QuantityOnHand.Should().Be(2);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Adjustment_Should_Set_Absolute_Quantity_And_Movement_AbsDelta()
    {
        var (tenantId, clinicId, productId, clinic, product) = SeedIdsAndEntities();
        SetupDefaultReads(tenantId, clinicId, product, clinic);
        var stock = new ProductStock(tenantId, clinicId, productId, 10m, 0);
        StockMovement? movement = null;
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .Callback<StockMovement, CancellationToken>((m, _) => movement = m)
            .ReturnsAsync((StockMovement e, CancellationToken _) => e);

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.Adjustment, 4), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stock.QuantityOnHand.Should().Be(4);
        movement!.Quantity.Should().Be(6);
        movement.MovementType.Should().Be(StockMovementType.Adjustment);
    }

    [Fact]
    public async Task Handle_Adjustment_Should_Create_Row_When_Missing()
    {
        var (tenantId, clinicId, productId, clinic, product) = SeedIdsAndEntities();
        SetupDefaultReads(tenantId, clinicId, product, clinic);
        ProductStock? added = null;
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductStock?)null);
        _productStocksWrite
            .Setup(r => r.AddAsync(It.IsAny<ProductStock>(), It.IsAny<CancellationToken>()))
            .Callback<ProductStock, CancellationToken>((s, _) => added = s)
            .ReturnsAsync((ProductStock e, CancellationToken _) => e);
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMovement e, CancellationToken _) => e);

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.Adjustment, 8), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.QuantityOnHand.Should().Be(8);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Adjustment_Should_Fail_Unchanged_Without_Persist()
    {
        var (tenantId, clinicId, productId, clinic, product) = SeedIdsAndEntities();
        SetupDefaultReads(tenantId, clinicId, product, clinic);
        var stock = new ProductStock(tenantId, clinicId, productId, 5m, 0);
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.Adjustment, 5), CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.AdjustmentUnchanged");
        _stockMovementsWrite.Verify(
            r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Map_Concurrency_To_ErrorCode()
    {
        var (tenantId, clinicId, productId, clinic, product) = SeedIdsAndEntities();
        SetupDefaultReads(tenantId, clinicId, product, clinic);
        _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductStock?)null);
        _productStocksWrite
            .Setup(r => r.AddAsync(It.IsAny<ProductStock>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductStock e, CancellationToken _) => e);
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMovement e, CancellationToken _) => e);
        _uow
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("Concurrency conflict.", innerException: null));

        var result = await CreateHandler().Handle(Cmd(clinicId, productId, StockMovementType.In, 1), CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.ConcurrencyConflict");
    }
}
