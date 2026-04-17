using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Behaviors;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Common.Behaviors;

public sealed class TenantSubscriptionWriteGuardBehaviorTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<TenantSubscription>> _subscriptions = new();

    private TenantSubscriptionWriteGuardBehavior<TRequest, Result> CreateBehavior<TRequest>()
        where TRequest : notnull
    {
        var evaluator = new TenantSubscriptionEffectiveWriteEvaluator(_tenants.Object, _subscriptions.Object);
        return new TenantSubscriptionWriteGuardBehavior<TRequest, Result>(_tenantContext.Object, evaluator);
    }

    private void SetupWritableTenant(Guid tenantId)
    {
        var tenant = new Tenant("Acme");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tenantId);
        var sub = TenantSubscription.StartTrial(tenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);

        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptions.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);
    }

    [Fact]
    public async Task Handle_Should_Allow_When_WritableTenant_And_MutationCommand()
    {
        var tenantId = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        SetupWritableTenant(tenantId);

        var behavior = CreateBehavior<SampleMutationCommand>();
        var nextCalled = false;

        var result = await behavior.Handle(new SampleMutationCommand(), _ =>
        {
            nextCalled = true;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Block_When_ReadOnlyTenant_And_MutationCommand()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant("Acme");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tenantId);
        var readOnlyByTrial = TenantSubscription.StartTrial(
            tenantId,
            SubscriptionPlanCode.Basic,
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            trialDays: 0);

        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptions.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(readOnlyByTrial);

        var behavior = CreateBehavior<SampleMutationCommand>();

        var result = await behavior.Handle(
            new SampleMutationCommand(),
            _ => Task.FromResult(Result.Success()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.TenantReadOnly");
    }

    [Fact]
    public async Task Handle_Should_Block_When_CancelledTenant_And_MutationCommand()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Tenant("Acme");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tenantId);
        var cancelled = TenantSubscription.StartTrial(tenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);
        typeof(TenantSubscription).GetProperty(nameof(TenantSubscription.Status))!
            .SetValue(cancelled, TenantSubscriptionStatus.Cancelled);

        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptions.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cancelled);

        var behavior = CreateBehavior<SampleMutationCommand>();
        var result = await behavior.Handle(
            new SampleMutationCommand(),
            _ => Task.FromResult(Result.Success()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.TenantCancelled");
    }

    [Fact]
    public async Task Handle_Should_Bypass_When_RequestIsQuery()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        var behavior = CreateBehavior<SampleQuery>();
        var nextCalled = false;

        var result = await behavior.Handle(new SampleQuery(), _ =>
        {
            nextCalled = true;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        _tenants.Verify(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _subscriptions.Verify(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Bypass_When_RequestHasIgnoreMarker()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        var behavior = CreateBehavior<IgnoredMutationCommand>();
        var nextCalled = false;

        var result = await behavior.Handle(new IgnoredMutationCommand(), _ =>
        {
            nextCalled = true;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        _tenants.Verify(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Bypass_When_AuthNamespaceCommand()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        var behavior = CreateBehavior<Backend.Veteriner.Application.Auth.TestDoubles.AuthLoginCommand>();
        var nextCalled = false;

        var result = await behavior.Handle(new Backend.Veteriner.Application.Auth.TestDoubles.AuthLoginCommand(), _ =>
        {
            nextCalled = true;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        _tenants.Verify(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Bypass_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);
        var behavior = CreateBehavior<SampleMutationCommand>();
        var nextCalled = false;

        var result = await behavior.Handle(new SampleMutationCommand(), _ =>
        {
            nextCalled = true;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        _tenants.Verify(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed record SampleMutationCommand;
    private sealed record SampleQuery;
    private sealed record IgnoredMutationCommand : IIgnoreTenantWriteSubscriptionGuard;
}
