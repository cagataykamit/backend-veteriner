using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.StockMovements.Commands.Reverse;
using Backend.Veteriner.Application.StockMovements.Commands.Reverse.Validators;
using Backend.Veteriner.Application.StockMovements.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.StockMovements.Handlers;

public sealed class ReverseStockMovementCommandHandlerTests
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

    public ReverseStockMovementCommandHandlerTests()
    {
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _clientContext.SetupGet(c => c.UserId).Returns(Guid.NewGuid());
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);

        var clinic = new Clinic(_tenantId, "Klinik", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, _clinicId);
        _product = new Product(_tenantId, "Ürün", "Adet", 10m, "TRY", DateTime.UtcNow, sku: "SKU-1");
        typeof(Product).GetProperty(nameof(Product.Id))!.SetValue(_product, _productId);

        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        _productsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_product);
        _productStocksWrite
            .Setup(r => r.AddAsync(It.IsAny<ProductStock>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductStock e, CancellationToken _) => e);
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMovement e, CancellationToken _) => e);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private ReverseStockMovementCommandHandler CreateHandler()
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
            StockBatchMocks.EmptyResolver(),
            _uow.Object,
            TimeProvider.System);

    private StockMovement Movement(
        StockMovementType type,
        decimal qty = 4m,
        string? referenceType = null,
        Guid? referenceId = null)
        => new(_tenantId, _clinicId, _productId, type, qty, DateTime.UtcNow,
            DateTime.UtcNow,
            referenceType: referenceType, referenceId: referenceId);

    private void SetupMovementById(StockMovement? movement)
        => _stockMovementsWrite
            .Setup(r => r.FirstOrDefaultAsync(
                It.Is<ISpecification<StockMovement>>(s => s is StockMovementByIdSpec),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(movement);

    private void SetupReversalOf(StockMovement? reversal)
        => _stockMovementsWrite
            .Setup(r => r.FirstOrDefaultAsync(
                It.Is<ISpecification<StockMovement>>(s => s is StockMovementReversalOfSpec),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(reversal);

    private void SetupByRequestId(StockMovement? movement)
        => _stockMovementsWrite
            .Setup(r => r.FirstOrDefaultAsync(
                It.Is<ISpecification<StockMovement>>(s => s is StockMovementByRequestIdSpec),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(movement);

    private void SetupStock(ProductStock? stock)
        => _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);

    private static ReverseStockMovementCommand Cmd(Guid id, Guid? requestId = null)
        => new(id, "Yanlış ürün seçildi", null, requestId);

    [Fact]
    public async Task Handle_Should_Fail_NotFound_When_Movement_Missing()
    {
        SetupMovementById(null);

        var result = await CreateHandler().Handle(Cmd(Guid.NewGuid()), CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Fail_ClinicContextMismatch()
    {
        var original = Movement(StockMovementType.Out);
        SetupMovementById(original);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(Cmd(original.Id), CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.ClinicContextMismatch");
    }

    [Fact]
    public async Task Handle_Out_Reversal_Should_Increase_Stock_And_Write_Reversal_With_Reference()
    {
        var referenceId = Guid.NewGuid();
        var original = Movement(StockMovementType.Out, 4m, "Administration", referenceId);
        SetupMovementById(original);
        var stock = new ProductStock(_tenantId, _clinicId, _productId, 6m, 0, DateTime.UtcNow);
        SetupStock(stock);
        StockMovement? added = null;
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .Callback<StockMovement, CancellationToken>((m, _) => added = m)
            .ReturnsAsync((StockMovement e, CancellationToken _) => e);

        var result = await CreateHandler().Handle(Cmd(original.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().BeTrue();
        stock.QuantityOnHand.Should().Be(10m);
        added!.MovementType.Should().Be(StockMovementType.Reversal);
        added.ReversesMovementId.Should().Be(original.Id);
        added.Quantity.Should().Be(4m);
        added.ReferenceType.Should().Be("Administration");
        added.ReferenceId.Should().Be(referenceId);
        added.Reason.Should().Be("Yanlış ürün seçildi");
        result.Value.Movement.ReversesMovementId.Should().Be(original.Id);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(StockMovementType.In)]
    [InlineData(StockMovementType.Initial)]
    public async Task Handle_In_Or_Initial_Reversal_Should_Decrease_Stock(StockMovementType type)
    {
        var original = Movement(type, 4m);
        SetupMovementById(original);
        var stock = new ProductStock(_tenantId, _clinicId, _productId, 10m, 0, DateTime.UtcNow);
        SetupStock(stock);

        var result = await CreateHandler().Handle(Cmd(original.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stock.QuantityOnHand.Should().Be(6m);
    }

    [Fact]
    public async Task Handle_In_Reversal_Should_Fail_InsufficientStock_When_Already_Consumed()
    {
        var original = Movement(StockMovementType.In, 4m);
        SetupMovementById(original);
        var stock = new ProductStock(_tenantId, _clinicId, _productId, 1m, 0, DateTime.UtcNow);
        SetupStock(stock);

        var result = await CreateHandler().Handle(Cmd(original.Id), CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.InsufficientStock");
        stock.QuantityOnHand.Should().Be(1m);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(StockMovementType.Adjustment)]
    [InlineData(StockMovementType.Reversal)]
    public async Task Handle_Should_Fail_NotReversible_For_Adjustment_And_Reversal(StockMovementType type)
    {
        var original = Movement(type);
        SetupMovementById(original);

        var result = await CreateHandler().Handle(Cmd(original.Id), CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.NotReversible");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_AlreadyReversed_And_NotTouchStock()
    {
        var original = Movement(StockMovementType.Out);
        SetupMovementById(original);
        SetupReversalOf(original.CreateReversal("x", null, DateTime.UtcNow, null, null, DateTime.UtcNow));
        var stock = new ProductStock(_tenantId, _clinicId, _productId, 6m, 0, DateTime.UtcNow);
        SetupStock(stock);

        var result = await CreateHandler().Handle(Cmd(original.Id), CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.AlreadyReversed");
        stock.QuantityOnHand.Should().Be(6m);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Replay_When_Same_RequestId_And_Same_Original()
    {
        var requestId = Guid.NewGuid();
        var original = Movement(StockMovementType.Out);
        SetupMovementById(original);
        var firstReversal = original.CreateReversal("x", null, DateTime.UtcNow, null, requestId, DateTime.UtcNow);
        SetupByRequestId(firstReversal);
        var stock = new ProductStock(_tenantId, _clinicId, _productId, 6m, 0, DateTime.UtcNow);
        SetupStock(stock);

        var result = await CreateHandler().Handle(Cmd(original.Id, requestId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().BeFalse();
        result.Value.Movement.Id.Should().Be(firstReversal.Id);
        stock.QuantityOnHand.Should().Be(6m);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_DuplicateRequestId_When_RequestId_Belongs_To_Other_Movement()
    {
        var requestId = Guid.NewGuid();
        var original = Movement(StockMovementType.Out);
        SetupMovementById(original);
        SetupByRequestId(Movement(StockMovementType.In));

        var result = await CreateHandler().Handle(Cmd(original.Id, requestId), CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.DuplicateRequestId");
    }

    [Fact]
    public async Task Handle_Should_Map_UniqueViolation_To_AlreadyReversed_When_Concurrent_Reversal_Wins()
    {
        var original = Movement(StockMovementType.Out);
        SetupMovementById(original);
        _stockMovementsWrite
            .SetupSequence(r => r.FirstOrDefaultAsync(
                It.Is<ISpecification<StockMovement>>(s => s is StockMovementReversalOfSpec),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMovement?)null)
            .ReturnsAsync(original.CreateReversal("x", null, DateTime.UtcNow, null, null, DateTime.UtcNow));
        SetupStock(new ProductStock(_tenantId, _clinicId, _productId, 6m, 0, DateTime.UtcNow));
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Cannot insert duplicate key.", innerException: null));

        var result = await CreateHandler().Handle(Cmd(original.Id), CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.AlreadyReversed");
    }

    [Fact]
    public async Task Handle_Should_Map_Concurrency_To_ErrorCode()
    {
        var original = Movement(StockMovementType.Out);
        SetupMovementById(original);
        SetupStock(new ProductStock(_tenantId, _clinicId, _productId, 6m, 0, DateTime.UtcNow));
        _uow
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("Concurrency conflict.", innerException: null));

        var result = await CreateHandler().Handle(Cmd(original.Id), CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.ConcurrencyConflict");
    }

    [Fact]
    public async Task Handle_Should_Reverse_Even_When_Product_Inactive()
    {
        var original = Movement(StockMovementType.Out);
        SetupMovementById(original);
        var stock = new ProductStock(_tenantId, _clinicId, _productId, 6m, 0, DateTime.UtcNow);
        SetupStock(stock);
        _product.Deactivate(DateTime.UtcNow);

        var result = await CreateHandler().Handle(Cmd(original.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stock.QuantityOnHand.Should().Be(6m + original.Quantity);
    }

    [Fact]
    public void Validator_Should_Require_Reason()
    {
        var validator = new ReverseStockMovementCommandValidator();

        validator.Validate(new ReverseStockMovementCommand(Guid.NewGuid(), "", null, null)).IsValid.Should().BeFalse();
        validator.Validate(new ReverseStockMovementCommand(Guid.NewGuid(), new string('a', 501), null, null)).IsValid.Should().BeFalse();
        validator.Validate(new ReverseStockMovementCommand(Guid.NewGuid(), "Gerekçe", null, null)).IsValid.Should().BeTrue();
    }
}
