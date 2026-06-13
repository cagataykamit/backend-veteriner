using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.StockMovements.Queries.GetList;
using Backend.Veteriner.Application.StockMovements.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.StockMovements.Handlers;

public sealed class StockMovementListQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<StockMovement>> _stockMovements = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private GetStockMovementsListQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _stockMovements.Object,
            _clinics.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetStockMovementsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _stockMovements.Verify(
            r => r.CountAsync(It.IsAny<ISpecification<StockMovement>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_NoClinicScope_Provided()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(new GetStockMovementsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("StockMovements.ClinicScopeRequired");
        _stockMovements.Verify(
            r => r.CountAsync(It.IsAny<StockMovementsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _stockMovements.Verify(
            r => r.ListAsync(It.IsAny<StockMovementsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_UseRequestClinicId_When_NoActiveContext()
    {
        var tid = Guid.NewGuid();
        var requestClinicId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _stockMovements.Setup(r => r.CountAsync(It.IsAny<StockMovementsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _stockMovements.Setup(r => r.ListAsync(It.IsAny<StockMovementsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockMovement>());

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler().Handle(
            new GetStockMovementsListQuery(page, ClinicId: requestClinicId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _stockMovements.Verify(
            r => r.CountAsync(It.IsAny<StockMovementsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_RequestClinic_Does_Not_Match_ActiveClinicContext()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        var queryClinic = Guid.NewGuid();
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(
            new GetStockMovementsListQuery(page, ClinicId: queryClinic),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("StockMovements.ClinicContextMismatch");
        _stockMovements.Verify(
            r => r.CountAsync(It.IsAny<StockMovementsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_PropagateScopeFailure_When_ResolverFails()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _scopeResolver.SetupAccessDenied();
        var page = new PageRequest { Page = 1, PageSize = 20 };

        var result = await CreateHandler().Handle(
            new GetStockMovementsListQuery(page, ClinicId: Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ClinicAdmin_Without_ClinicScope()
    {
        var tid = Guid.NewGuid();
        var assigned = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var resolver = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { assigned, c2 });
        var handler = new GetStockMovementsListQueryHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver.Object,
            _stockMovements.Object,
            _clinics.Object);

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await handler.Handle(new GetStockMovementsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("StockMovements.ClinicScopeRequired");
        _stockMovements.Verify(
            r => r.CountAsync(It.IsAny<StockMovementsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        resolver.Verify(
            x => x.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_MapRows_From_ProductNavigation()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var occurred = DateTime.UtcNow.AddHours(-2);
        var product = new Product(tid, "Asi", "Adet", 100m, "TRY");
        typeof(Product).GetProperty(nameof(Product.Category))!.SetValue(product, null);

        var movement = new StockMovement(
            tid,
            clinicId,
            product.Id,
            StockMovementType.In,
            quantity: 3m,
            occurredAtUtc: occurred,
            unitCost: 50m,
            reason: "Alim",
            notes: "lot-a");

        typeof(StockMovement).GetProperty(nameof(StockMovement.Product))!.SetValue(movement, product);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        _stockMovements.Setup(r => r.CountAsync(It.IsAny<StockMovementsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _stockMovements.Setup(r => r.ListAsync(It.IsAny<StockMovementsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockMovement> { movement });
        _clinics.Setup(r => r.ListAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic> { CreateClinicReflection(clinicId, tid, "Main") });

        var page = new PageRequest { Page = 1, PageSize = 20 };
        var result = await CreateHandler().Handle(new GetStockMovementsListQuery(page), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!.Items.Should().ContainSingle().Subject;
        dto.ProductName.Should().Be("Asi");
        dto.MovementType.Should().Be(StockMovementType.In);
        dto.Quantity.Should().Be(3);
        dto.UnitCost.Should().Be(50);
        dto.Reason.Should().Be("Alim");
        dto.ClinicName.Should().Be("Main");
        dto.OccurredAtUtc.Should().Be(occurred);
    }

    [Fact]
    public async Task Handle_Should_CallRepository_With_FilterSpecs_When_ScopeResolved()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var resolver = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { clinicId });
        _stockMovements.Setup(r => r.CountAsync(It.IsAny<StockMovementsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _stockMovements.Setup(r => r.ListAsync(It.IsAny<StockMovementsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockMovement>());

        var handler = new GetStockMovementsListQueryHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            resolver.Object,
            _stockMovements.Object,
            _clinics.Object);

        var pid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var from = DateTime.UtcNow.AddDays(-5);
        var to = DateTime.UtcNow;

        var page = new PageRequest { Page = 2, PageSize = 50 };
        await handler.Handle(
            new GetStockMovementsListQuery(
                page,
                ClinicId: clinicId,
                ProductId: pid,
                ProductCategoryId: cid,
                MovementType: StockMovementType.Out,
                DateFromUtc: from,
                DateToUtc: to),
            CancellationToken.None);

        _stockMovements.Verify(
            r => r.CountAsync(It.IsAny<StockMovementsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _stockMovements.Verify(
            r => r.ListAsync(It.IsAny<StockMovementsFilteredPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Clinic CreateClinicReflection(Guid id, Guid tenantId, string name)
    {
        var clinic = new Clinic(tenantId, name, "Istanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, id);
        return clinic;
    }
}
