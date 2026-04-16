using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SpeciesReference.Commands.Create;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Catalog;

public sealed class CreateSpeciesCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<TenantSubscription>> _subscriptions = new();
    private readonly Mock<IReadRepository<Species>> _read = new();
    private readonly Mock<IRepository<Species>> _write = new();

    private CreateSpeciesCommandHandler CreateHandler()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant("X");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tenantId);
        var sub = TenantSubscription.StartTrial(tenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);

        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Backend.Veteriner.Application.Tenants.Specs.TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptions.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Backend.Veteriner.Application.Tenants.Specs.TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);

        var writeEvaluator = new TenantSubscriptionEffectiveWriteEvaluator(_tenants.Object, _subscriptions.Object);
        return new(_tenantContext.Object, writeEvaluator, _read.Object, _write.Object);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateCode()
    {
        var handler = CreateHandler();
        var command = new CreateSpeciesCommand("DOG", "Köpek", 0);

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByCodeSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Species("DOG", "Mevcut"));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Species.DuplicateCode");
        _write.Verify(r => r.AddAsync(It.IsAny<Species>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateName_CaseInsensitive()
    {
        var handler = CreateHandler();
        var command = new CreateSpeciesCommand("NEW", "Köpek", 0);

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByCodeSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Species?)null);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByNameLowerSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Species("DOG", "köpek"));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Species.DuplicateName");
        _write.Verify(r => r.AddAsync(It.IsAny<Species>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Create_When_Unique()
    {
        var handler = CreateHandler();
        var command = new CreateSpeciesCommand("DOG", "Köpek", 5);

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByCodeSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Species?)null);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByNameLowerSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Species?)null);

        Species? captured = null;
        _write.Setup(r => r.AddAsync(It.IsAny<Species>(), It.IsAny<CancellationToken>()))
            .Callback<Species, CancellationToken>((s, _) => captured = s);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Code.Should().Be("DOG");
        captured.Name.Should().Be("Köpek");
        captured.DisplayOrder.Should().Be(5);
        _write.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);
        var writeEvaluator = new TenantSubscriptionEffectiveWriteEvaluator(_tenants.Object, _subscriptions.Object);
        var handler = new CreateSpeciesCommandHandler(_tenantContext.Object, writeEvaluator, _read.Object, _write.Object);

        var result = await handler.Handle(new CreateSpeciesCommand("DOG", "Köpek", 0), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _write.Verify(r => r.AddAsync(It.IsAny<Species>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_SubscriptionReadOnly()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant("X");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tenantId);
        var sub = TenantSubscription.StartTrial(
            tenantId,
            SubscriptionPlanCode.Basic,
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            trialDays: 0);

        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Backend.Veteriner.Application.Tenants.Specs.TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptions.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Backend.Veteriner.Application.Tenants.Specs.TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);

        var writeEvaluator = new TenantSubscriptionEffectiveWriteEvaluator(_tenants.Object, _subscriptions.Object);
        var handler = new CreateSpeciesCommandHandler(_tenantContext.Object, writeEvaluator, _read.Object, _write.Object);

        var result = await handler.Handle(new CreateSpeciesCommand("DOG", "Köpek", 0), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.TenantReadOnly");
        _write.Verify(r => r.AddAsync(It.IsAny<Species>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
