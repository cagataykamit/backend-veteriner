using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.StockMovements.Commands.Create;
using Backend.Veteriner.Application.StockMovements.Commands.Create.Validators;
using Backend.Veteriner.Application.StockMovements.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.StockMovements.Handlers;

/// <summary>Aşama 5 Dilim 1: requestId tekrarı, kaynak benzersizliği ve eşzamanlı ilk giriş.</summary>
public sealed class CreateStockMovementSafetyTests
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

    public CreateStockMovementSafetyTests()
    {
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _clientContext.SetupGet(c => c.UserId).Returns((Guid?)null);
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);

        var clinic = new Clinic(_tenantId, "Klinik", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, _clinicId);
        var product = new Product(_tenantId, "Ürün", "Adet", 10m, "TRY", sku: "SKU-1");
        typeof(Product).GetProperty(nameof(Product.Id))!.SetValue(product, _productId);

        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        _productsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _productStocksWrite
            .Setup(r => r.AddAsync(It.IsAny<ProductStock>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductStock e, CancellationToken _) => e);
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMovement e, CancellationToken _) => e);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private CreateStockMovementCommandHandler CreateHandler()
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
            new Mock<IRepository<StockBatch>>().Object,
            StockBatchMocks.EmptyResolver(),
            _uow.Object,
            StockBatchMocks.Clock);

    private CreateStockMovementCommand Cmd(
        StockMovementType type,
        decimal qty,
        Guid? requestId = null,
        string? referenceType = null,
        Guid? referenceId = null)
        => new(
            _clinicId,
            _productId,
            type,
            qty,
            UnitCost: null,
            Reason: null,
            ReferenceType: referenceType,
            ReferenceId: referenceId,
            OccurredAtUtc: null,
            Notes: null,
            RequestId: requestId);

    private StockMovement ExistingMovement(StockMovementType type, Guid? requestId = null)
        => new(_tenantId, _clinicId, _productId, type, 3m, DateTime.UtcNow, requestId: requestId);

    private void SetupStock(ProductStock? stock)
        => _productStocksWrite
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);

    private void SetupMovementByRequestId(StockMovement? movement)
        => _stockMovementsWrite
            .Setup(r => r.FirstOrDefaultAsync(
                It.Is<ISpecification<StockMovement>>(s => s is StockMovementByRequestIdSpec),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(movement);

    private void SetupMovementByReference(StockMovement? movement)
        => _stockMovementsWrite
            .Setup(r => r.FirstOrDefaultAsync(
                It.Is<ISpecification<StockMovement>>(s => s is StockMovementByReferenceSpec),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(movement);

    [Fact]
    public async Task Handle_Should_ReturnExistingMovement_And_NotTouchStock_When_RequestId_Repeats()
    {
        var requestId = Guid.NewGuid();
        var existing = ExistingMovement(StockMovementType.Out, requestId);
        SetupMovementByRequestId(existing);
        var stock = new ProductStock(_tenantId, _clinicId, _productId, 10m, 0);
        SetupStock(stock);

        var result = await CreateHandler().Handle(Cmd(StockMovementType.Out, 3, requestId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().BeFalse();
        result.Value.Movement.Id.Should().Be(existing.Id);
        result.Value.Movement.RequestId.Should().Be(requestId);
        stock.QuantityOnHand.Should().Be(10m);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _stockMovementsWrite.Verify(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_DuplicateRequestId_When_RequestId_UsedForDifferentMovementType()
    {
        var requestId = Guid.NewGuid();
        SetupMovementByRequestId(ExistingMovement(StockMovementType.In, requestId));

        var result = await CreateHandler().Handle(Cmd(StockMovementType.Out, 3, requestId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("StockMovements.DuplicateRequestId");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Persist_RequestId_On_NewMovement()
    {
        var requestId = Guid.NewGuid();
        SetupStock(null);
        StockMovement? added = null;
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .Callback<StockMovement, CancellationToken>((m, _) => added = m)
            .ReturnsAsync((StockMovement e, CancellationToken _) => e);

        var result = await CreateHandler().Handle(Cmd(StockMovementType.In, 2, requestId), CancellationToken.None);

        result.Value!.Created.Should().BeTrue();
        added!.RequestId.Should().Be(requestId);
        result.Value.Movement.RequestId.Should().Be(requestId);
    }

    [Fact]
    public async Task Handle_Should_Fail_DuplicateReference_And_NotSave_When_Reference_Exists()
    {
        var referenceId = Guid.NewGuid();
        SetupMovementByReference(ExistingMovement(StockMovementType.Out));
        SetupStock(new ProductStock(_tenantId, _clinicId, _productId, 10m, 0));

        var result = await CreateHandler().Handle(
            Cmd(StockMovementType.Out, 1, referenceType: "Administration", referenceId: referenceId),
            CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.DuplicateReference");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Replay_Instead_Of_DuplicateReference_When_Same_RequestId()
    {
        var requestId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var existing = ExistingMovement(StockMovementType.Out, requestId);
        SetupMovementByRequestId(existing);
        SetupMovementByReference(existing);

        var result = await CreateHandler().Handle(
            Cmd(StockMovementType.Out, 1, requestId, "Administration", referenceId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Should_Map_UniqueViolation_On_FirstStockRow_To_ConcurrencyConflict()
    {
        // Satır yokken eşzamanlı ilk giriş: SaveChanges ProductStocks benzersiz indeksine çarpar, sonra satır görünür.
        var winner = new ProductStock(_tenantId, _clinicId, _productId, 5m, 0);
        _productStocksWrite
            .SetupSequence(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductStock>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductStock?)null)
            .ReturnsAsync(winner);
        _productStocksWrite
            .Setup(r => r.AddAsync(It.IsAny<ProductStock>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Cannot insert duplicate key.", innerException: null));

        var result = await CreateHandler().Handle(Cmd(StockMovementType.In, 1), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("StockMovements.ConcurrencyConflict");
    }

    [Fact]
    public async Task Handle_Should_Replay_Winner_When_Concurrent_Request_With_Same_RequestId_Wins()
    {
        var requestId = Guid.NewGuid();
        var winnerMovement = ExistingMovement(StockMovementType.In, requestId);
        _stockMovementsWrite
            .SetupSequence(r => r.FirstOrDefaultAsync(
                It.Is<ISpecification<StockMovement>>(s => s is StockMovementByRequestIdSpec),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMovement?)null)
            .ReturnsAsync(winnerMovement);
        SetupStock(null);
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Cannot insert duplicate key.", innerException: null));

        var result = await CreateHandler().Handle(Cmd(StockMovementType.In, 3, requestId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().BeFalse();
        result.Value.Movement.Id.Should().Be(winnerMovement.Id);
    }

    [Fact]
    public async Task Handle_Should_Fail_DuplicateReference_When_Concurrent_Reference_Wins()
    {
        var referenceId = Guid.NewGuid();
        _stockMovementsWrite
            .SetupSequence(r => r.FirstOrDefaultAsync(
                It.Is<ISpecification<StockMovement>>(s => s is StockMovementByReferenceSpec),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockMovement?)null)
            .ReturnsAsync(ExistingMovement(StockMovementType.Out));
        SetupStock(new ProductStock(_tenantId, _clinicId, _productId, 10m, 0));
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Cannot insert duplicate key.", innerException: null));

        var result = await CreateHandler().Handle(
            Cmd(StockMovementType.Out, 1, referenceType: "Administration", referenceId: referenceId),
            CancellationToken.None);

        result.Error!.Code.Should().Be("StockMovements.DuplicateReference");
    }

    [Fact]
    public async Task Handle_Should_Rethrow_Unrecognized_DbUpdateException()
    {
        SetupStock(new ProductStock(_tenantId, _clinicId, _productId, 10m, 0));
        _stockMovementsWrite
            .Setup(r => r.AddAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Başka hata.", innerException: null));

        var act = () => CreateHandler().Handle(Cmd(StockMovementType.In, 1), CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public void Validator_Should_Reject_Reversal_Type_And_Incomplete_Reference()
    {
        var validator = new CreateStockMovementCommandValidator();

        validator.Validate(Cmd(StockMovementType.Reversal, 1)).IsValid.Should().BeFalse();
        validator.Validate(Cmd(StockMovementType.In, 1, referenceType: "X")).IsValid.Should().BeFalse();
        validator.Validate(Cmd(StockMovementType.In, 1, referenceId: Guid.NewGuid())).IsValid.Should().BeFalse();
        validator.Validate(Cmd(StockMovementType.In, 1, referenceType: "X", referenceId: Guid.NewGuid())).IsValid.Should().BeTrue();
        validator.Validate(Cmd(StockMovementType.In, 1)).IsValid.Should().BeTrue();
    }
}
