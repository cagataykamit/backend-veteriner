using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.StockMovements.Queries.GetByProductId;
using Backend.Veteriner.Application.StockMovements.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.StockMovements.Handlers;

public sealed class StockMovementByProductIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Product>> _products = new();
    private readonly Mock<IReadRepository<ProductCategory>> _categories = new();
    private readonly Mock<IReadRepository<StockMovement>> _stockMovements = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private GetStockMovementsByProductIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _products.Object,
            _categories.Object,
            _stockMovements.Object,
            _clinics.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(
            new GetStockMovementsByProductIdQuery(Guid.NewGuid(), page),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _stockMovements.Verify(
            r => r.ListAsync(It.IsAny<StockMovementsForProductFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnProductsNotFound_When_ProductMissing()
    {
        var tid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var page = new PageRequest { Page = 1, PageSize = 20 };
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _products.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        var result = await CreateHandler().Handle(new GetStockMovementsByProductIdQuery(pid, page), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Products.NotFound");
        _stockMovements.Verify(
            r => r.ListAsync(It.IsAny<StockMovementsForProductFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ClinicContextMismatch()
    {
        var tid = Guid.NewGuid();
        var product = new Product(tid, "P", "Adet", 1m, "TRY");
        var page = new PageRequest { Page = 1, PageSize = 20 };
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        _products.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var result = await CreateHandler().Handle(
            new GetStockMovementsByProductIdQuery(product.Id, page, ClinicId: Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("StockMovements.ClinicContextMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnPagedRows_When_ProductExists()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var product = new Product(tid, "Vitamin", "Adet", 20m, "TRY");
        var occurred = DateTime.UtcNow.AddMinutes(-30);
        var movement = new StockMovement(
            tid,
            clinicId,
            product.Id,
            StockMovementType.Adjustment,
            1m,
            occurred);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _products.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        _categories.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<ProductCategory>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductCategory?)null);
        _stockMovements.Setup(r => r.CountAsync(It.IsAny<StockMovementsForProductFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _stockMovements.Setup(r => r.ListAsync(It.IsAny<StockMovementsForProductFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockMovement> { movement });
        _clinics.Setup(r => r.ListAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>
            {
                CreateClinicReflection(clinicId, tid, "Depo")
            });

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler().Handle(new GetStockMovementsByProductIdQuery(product.Id, page), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!.Items.Should().ContainSingle().Subject;
        dto.ProductId.Should().Be(product.Id);
        dto.ProductName.Should().Be("Vitamin");
        dto.MovementType.Should().Be(StockMovementType.Adjustment);
        dto.ClinicId.Should().Be(clinicId);
        dto.ClinicName.Should().Be("Depo");
        result.Value.TotalItems.Should().Be(1);
    }

    private static Clinic CreateClinicReflection(Guid id, Guid tenantId, string name)
    {
        var clinic = new Clinic(tenantId, name, "Ankara");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, id);
        return clinic;
    }
}
