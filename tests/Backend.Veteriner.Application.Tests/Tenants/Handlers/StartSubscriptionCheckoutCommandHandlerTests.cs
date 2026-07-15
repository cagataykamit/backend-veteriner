using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Tenants.Commands.Checkout;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class StartSubscriptionCheckoutCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<TenantSubscription>> _subscriptionsRead = new();
    private readonly Mock<IRepository<TenantSubscription>> _subscriptionsWrite = new();
    private readonly Mock<IRepository<ScheduledSubscriptionPlanChange>> _planChangesWrite = new();
    private readonly Mock<IReadRepository<ScheduledSubscriptionPlanChange>> _planChangesRead = new();
    private readonly Mock<IRepository<BillingCheckoutSession>> _checkoutSessionsWrite = new();
    private readonly Mock<IReadRepository<BillingCheckoutSession>> _checkoutSessionsRead = new();
    private readonly Mock<IBillingCheckoutProviderResolver> _resolver = new();

    private StartSubscriptionCheckoutCommandHandler CreateHandler(BillingOptions billing)
        => new(
            _tenantContext.Object,
            _tenants.Object,
            _subscriptionsRead.Object,
            _subscriptionsWrite.Object,
            _planChangesWrite.Object,
            _planChangesRead.Object,
            _checkoutSessionsWrite.Object,
            _checkoutSessionsRead.Object,
            Options.Create(billing),
            _resolver.Object,
            NullLogger<StartSubscriptionCheckoutCommandHandler>.Instance);

    private static BillingOptions CreateBillingOptions()
        => new()
        {
            DefaultCheckoutProvider = "Manual",
            PlanPricesMinor = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["Basic"] = 10_000L,
                ["Pro"] = 20_000L,
                ["Premium"] = 30_000L,
            },
        };

    private static Tenant CreateTenant(Guid tenantId)
    {
        var tenant = new Tenant("T");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tenantId);
        return tenant;
    }

    private static TenantSubscription CreateExpiredReadOnlyPaidSubscription(
        Guid tenantId,
        SubscriptionPlanCode planCode)
    {
        var sub = TenantSubscription.StartTrial(tenantId, planCode, DateTime.UtcNow.AddDays(-60), 14);
        sub.ActivatePaidPlan(planCode, DateTime.UtcNow.AddDays(-46));
        typeof(TenantSubscription).GetProperty(nameof(TenantSubscription.Status))!
            .SetValue(sub, TenantSubscriptionStatus.ReadOnly);
        return sub;
    }

    private void SetupCheckoutInfrastructure(
        Guid tenantId,
        Tenant tenant,
        TenantSubscription sub,
        BillingCheckoutSession? existingOpenSession = null)
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);
        _planChangesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenScheduledPlanChangeByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledSubscriptionPlanChange?)null);
        _checkoutSessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenBillingCheckoutSessionByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOpenSession);
    }

    private void SetupManualCheckoutProvider(long chargeMinor = 30_000L)
    {
        var manualProvider = new Mock<IBillingCheckoutProvider>();
        manualProvider.SetupGet(x => x.Provider).Returns(BillingProvider.Manual);
        manualProvider
            .Setup(x => x.PrepareCheckoutAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CheckoutPrepareResult>.Success(new CheckoutPrepareResult("https://checkout/new", "ext-new", "TRY", chargeMinor, 1.0m)));
        _resolver.Setup(x => x.Resolve(BillingProvider.Manual)).Returns(manualProvider.Object);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantMismatch()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(a);

        var billing = new BillingOptions { DefaultCheckoutProvider = "Manual" };
        var handler = CreateHandler(billing);
        var result = await handler.Handle(new StartSubscriptionCheckoutCommand(b, "Pro"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_SubscriptionCancelled()
    {
        var tid = Guid.NewGuid();
        var tenant = new Tenant("T");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        var sub = TenantSubscription.StartTrial(tid, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);
        typeof(TenantSubscription).GetProperty(nameof(TenantSubscription.Status))!
            .SetValue(sub, TenantSubscriptionStatus.Cancelled);

        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);

        var billing = new BillingOptions { DefaultCheckoutProvider = "Manual" };
        var handler = CreateHandler(billing);
        var result = await handler.Handle(new StartSubscriptionCheckoutCommand(tid, "Pro"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.TenantCancelled");
    }

    [Fact]
    public async Task Handle_Should_ReturnSuccess_When_ManualUpgrade_And_PricesConfigured()
    {
        var tid = Guid.NewGuid();
        var tenant = new Tenant("T");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        // Paid/active tenant: trial dönemindeki tenantlar artık proration/checkout akışına girmiyor
        // (bkz. Trial_* testleri); bu test var olan ücretli abonelikte upgrade proration davranışını korur.
        var sub = TenantSubscription.StartTrial(tid, SubscriptionPlanCode.Basic, DateTime.UtcNow.AddDays(-30), 14);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Basic, DateTime.UtcNow.AddDays(-16));

        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);
        _planChangesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenScheduledPlanChangeByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledSubscriptionPlanChange?)null);
        _checkoutSessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenBillingCheckoutSessionByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingCheckoutSession?)null);

        var manualProvider = new Mock<IBillingCheckoutProvider>();
        manualProvider.SetupGet(x => x.Provider).Returns(BillingProvider.Manual);
        manualProvider
            .Setup(x => x.PrepareCheckoutAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CheckoutPrepareResult>.Success(new CheckoutPrepareResult(null, null, "TRY", 1000L, 0.5m)));
        _resolver.Setup(x => x.Resolve(BillingProvider.Manual)).Returns(manualProvider.Object);

        var billing = new BillingOptions
        {
            DefaultCheckoutProvider = "Manual",
            PlanPricesMinor = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["Basic"] = 10_000L,
                ["Pro"] = 20_000L,
            },
        };

        var handler = CreateHandler(billing);
        var result = await handler.Handle(new StartSubscriptionCheckoutCommand(tid, "Pro"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TargetPlanCode.Should().Be("Pro");
        result.Value.CurrentPlanCode.Should().Be("Basic");
        _checkoutSessionsWrite.Verify(x => x.AddAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReuseOpenCheckout_When_SameTarget_And_NotExpired()
    {
        var tid = Guid.NewGuid();
        var tenant = new Tenant("T");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        var sub = TenantSubscription.StartTrial(tid, SubscriptionPlanCode.Basic, DateTime.UtcNow.AddDays(-30), 14);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Basic, DateTime.UtcNow.AddDays(-16));

        var created = DateTime.UtcNow.AddMinutes(-10);
        var expires = DateTime.UtcNow.AddHours(2);
        var session = BillingCheckoutSession.CreatePending(
            tid,
            SubscriptionPlanCode.Basic,
            SubscriptionPlanCode.Pro,
            BillingProvider.Manual,
            created,
            expires);
        session.SetRedirectReady("https://checkout/reuse", "ext-1", created.AddMinutes(1));

        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);
        _planChangesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenScheduledPlanChangeByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledSubscriptionPlanChange?)null);
        _checkoutSessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenBillingCheckoutSessionByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var billing = new BillingOptions
        {
            DefaultCheckoutProvider = "Manual",
            PlanPricesMinor = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["Basic"] = 10_000L,
                ["Pro"] = 20_000L,
            },
        };

        var handler = CreateHandler(billing);
        var result = await handler.Handle(new StartSubscriptionCheckoutCommand(tid, "Pro"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CheckoutSessionId.Should().Be(session.Id);
        result.Value.CheckoutUrl.Should().Be("https://checkout/reuse");
        _resolver.Verify(x => x.Resolve(It.IsAny<BillingProvider>()), Times.Never);
        _checkoutSessionsWrite.Verify(x => x.AddAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Never);
        _checkoutSessionsWrite.Verify(x => x.UpdateAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_MarkExpiredAndCreateNew_When_OpenCheckoutSameTarget_ButExpired()
    {
        var tid = Guid.NewGuid();
        var tenant = new Tenant("T");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        var sub = TenantSubscription.StartTrial(tid, SubscriptionPlanCode.Basic, DateTime.UtcNow.AddDays(-30), 14);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Basic, DateTime.UtcNow.AddDays(-16));

        var created = DateTime.UtcNow.AddHours(-4);
        var expires = created.AddMinutes(30);
        var stale = BillingCheckoutSession.CreatePending(
            tid,
            SubscriptionPlanCode.Basic,
            SubscriptionPlanCode.Pro,
            BillingProvider.Manual,
            created,
            expires);
        stale.SetRedirectReady("https://iyzico/old-token", "tok-old", created.AddMinutes(1));

        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);
        _planChangesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenScheduledPlanChangeByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledSubscriptionPlanChange?)null);
        _checkoutSessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenBillingCheckoutSessionByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stale);

        var manualProvider = new Mock<IBillingCheckoutProvider>();
        manualProvider.SetupGet(x => x.Provider).Returns(BillingProvider.Manual);
        manualProvider
            .Setup(x => x.PrepareCheckoutAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CheckoutPrepareResult>.Success(new CheckoutPrepareResult("https://iyzico/new-token", "tok-new", "TRY", 1000L, 0.5m)));
        _resolver.Setup(x => x.Resolve(BillingProvider.Manual)).Returns(manualProvider.Object);

        var billing = new BillingOptions
        {
            DefaultCheckoutProvider = "Manual",
            PlanPricesMinor = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["Basic"] = 10_000L,
                ["Pro"] = 20_000L,
            },
        };

        var handler = CreateHandler(billing);
        var result = await handler.Handle(new StartSubscriptionCheckoutCommand(tid, "Pro"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CheckoutSessionId.Should().NotBe(stale.Id);
        result.Value.CheckoutUrl.Should().Be("https://iyzico/new-token");
        stale.Status.Should().Be(BillingCheckoutSessionStatus.Expired);
        _checkoutSessionsWrite.Verify(x => x.UpdateAsync(stale, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _checkoutSessionsWrite.Verify(x => x.AddAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Once);
        manualProvider.Verify(
            x => x.PrepareCheckoutAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // BILLING-LIVE-1: Trial dönemindeki tenant plan yükseltirse ödeme farkı hesaplanmamalı ve
    // checkout session oluşturulmamalıdır (public vaat: 14 gün ödeme alınmaz).
    [Theory]
    [InlineData("Pro")]
    [InlineData("Premium")]
    public async Task Handle_Should_ApplyPlanDirectly_Without_Checkout_Or_Proration_When_TenantIsTrialing(string targetPlan)
    {
        var tid = Guid.NewGuid();
        var tenant = new Tenant("T");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        var trialStart = DateTime.UtcNow.AddDays(-7);
        var sub = TenantSubscription.StartTrial(tid, SubscriptionPlanCode.Basic, trialStart, 14);
        var expectedTrialEnd = sub.TrialEndsAtUtc;

        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);
        _planChangesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenScheduledPlanChangeByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledSubscriptionPlanChange?)null);
        _checkoutSessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenBillingCheckoutSessionByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingCheckoutSession?)null);

        // Fiyat yapılandırılmış olsa bile trial'da kullanılmamalı (proration asla hesaplanmamalı).
        var billing = new BillingOptions
        {
            DefaultCheckoutProvider = "Manual",
            PlanPricesMinor = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["Basic"] = 10_000L,
                ["Pro"] = 20_000L,
                ["Premium"] = 30_000L,
            },
        };

        var handler = CreateHandler(billing);
        var result = await handler.Handle(new StartSubscriptionCheckoutCommand(tid, targetPlan), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TrialPlanChangeApplied.Should().BeTrue();
        result.Value.CheckoutSessionId.Should().BeNull();
        result.Value.CheckoutUrl.Should().BeNull();
        result.Value.CanContinue.Should().BeFalse();
        result.Value.Status.Should().BeNull();
        result.Value.ProratedChargeMinor.Should().Be(0);
        result.Value.ProrationRatio.Should().BeNull();
        result.Value.ChargeCurrencyCode.Should().BeNull();
        result.Value.TargetPlanCode.Should().Be(targetPlan);
        result.Value.CurrentPlanCode.Should().Be("Basic");

        sub.PlanCode.Should().Be(Enum.Parse<SubscriptionPlanCode>(targetPlan));
        sub.Status.Should().Be(TenantSubscriptionStatus.Trialing);
        sub.TrialStartsAtUtc.Should().Be(trialStart);
        sub.TrialEndsAtUtc.Should().Be(expectedTrialEnd);

        _subscriptionsWrite.Verify(x => x.UpdateAsync(sub, It.IsAny<CancellationToken>()), Times.Once);
        _checkoutSessionsWrite.Verify(x => x.AddAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Never);
        _resolver.Verify(x => x.Resolve(It.IsAny<BillingProvider>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_CancelOpenCheckoutSession_When_TrialUpgradeAppliedDirectly()
    {
        var tid = Guid.NewGuid();
        var tenant = new Tenant("T");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        var sub = TenantSubscription.StartTrial(tid, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);

        var staleSession = BillingCheckoutSession.CreatePending(
            tid,
            SubscriptionPlanCode.Basic,
            SubscriptionPlanCode.Pro,
            BillingProvider.Manual,
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow.AddMinutes(25));

        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);
        _planChangesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenScheduledPlanChangeByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledSubscriptionPlanChange?)null);
        _checkoutSessionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OpenBillingCheckoutSessionByTenantSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleSession);

        var billing = new BillingOptions { DefaultCheckoutProvider = "Manual" };
        var handler = CreateHandler(billing);
        var result = await handler.Handle(new StartSubscriptionCheckoutCommand(tid, "Pro"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TrialPlanChangeApplied.Should().BeTrue();
        staleSession.Status.Should().Be(BillingCheckoutSessionStatus.Cancelled);
        _checkoutSessionsWrite.Verify(x => x.UpdateAsync(staleSession, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TrialTenant_RequestsSamePlan_NoOp()
    {
        var tid = Guid.NewGuid();
        var tenant = new Tenant("T");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        var sub = TenantSubscription.StartTrial(tid, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14);

        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _tenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _subscriptionsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sub);

        var billing = new BillingOptions { DefaultCheckoutProvider = "Manual" };
        var handler = CreateHandler(billing);
        var result = await handler.Handle(new StartSubscriptionCheckoutCommand(tid, "Basic"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.SamePlanAlreadyActive");
        _subscriptionsWrite.Verify(x => x.UpdateAsync(It.IsAny<TenantSubscription>(), It.IsAny<CancellationToken>()), Times.Never);
        _checkoutSessionsWrite.Verify(x => x.AddAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnSuccess_When_ExpiredReadOnly_RequestsSamePlanRenewal()
    {
        var tid = Guid.NewGuid();
        var tenant = CreateTenant(tid);
        var sub = CreateExpiredReadOnlyPaidSubscription(tid, SubscriptionPlanCode.Premium);
        SetupCheckoutInfrastructure(tid, tenant, sub);
        SetupManualCheckoutProvider(30_000L);

        var handler = CreateHandler(CreateBillingOptions());
        var result = await handler.Handle(new StartSubscriptionCheckoutCommand(tid, "Premium"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TargetPlanCode.Should().Be("Premium");
        result.Value.CurrentPlanCode.Should().Be("Premium");
        result.Value.ProratedChargeMinor.Should().Be(30_000L);
        result.Value.ProrationRatio.Should().Be(1.0m);
        result.Value.TrialPlanChangeApplied.Should().BeFalse();
        _checkoutSessionsWrite.Verify(x => x.AddAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnSuccess_When_ExpiredReadOnly_RequestsLowerPlan()
    {
        var tid = Guid.NewGuid();
        var tenant = CreateTenant(tid);
        var sub = CreateExpiredReadOnlyPaidSubscription(tid, SubscriptionPlanCode.Premium);
        SetupCheckoutInfrastructure(tid, tenant, sub);
        SetupManualCheckoutProvider(10_000L);

        var handler = CreateHandler(CreateBillingOptions());
        var result = await handler.Handle(new StartSubscriptionCheckoutCommand(tid, "Basic"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TargetPlanCode.Should().Be("Basic");
        result.Value.CurrentPlanCode.Should().Be("Premium");
        result.Value.ProratedChargeMinor.Should().Be(10_000L);
        result.Value.ProrationRatio.Should().Be(1.0m);
        _checkoutSessionsWrite.Verify(x => x.AddAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnSuccess_When_ExpiredReadOnly_RequestsHigherPlan()
    {
        var tid = Guid.NewGuid();
        var tenant = CreateTenant(tid);
        var sub = CreateExpiredReadOnlyPaidSubscription(tid, SubscriptionPlanCode.Basic);
        SetupCheckoutInfrastructure(tid, tenant, sub);
        SetupManualCheckoutProvider();

        var handler = CreateHandler(CreateBillingOptions());
        var result = await handler.Handle(new StartSubscriptionCheckoutCommand(tid, "Pro"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TargetPlanCode.Should().Be("Pro");
        result.Value.CurrentPlanCode.Should().Be("Basic");
        _checkoutSessionsWrite.Verify(x => x.AddAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ActivePaid_RequestsSamePlan()
    {
        var tid = Guid.NewGuid();
        var tenant = CreateTenant(tid);
        var sub = TenantSubscription.StartTrial(tid, SubscriptionPlanCode.Premium, DateTime.UtcNow.AddDays(-30), 14);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Premium, DateTime.UtcNow.AddDays(-16));

        SetupCheckoutInfrastructure(tid, tenant, sub);

        var handler = CreateHandler(CreateBillingOptions());
        var result = await handler.Handle(new StartSubscriptionCheckoutCommand(tid, "Premium"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.SamePlanAlreadyActive");
        _checkoutSessionsWrite.Verify(x => x.AddAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ActivePaid_RequestsDowngrade()
    {
        var tid = Guid.NewGuid();
        var tenant = CreateTenant(tid);
        var sub = TenantSubscription.StartTrial(tid, SubscriptionPlanCode.Premium, DateTime.UtcNow.AddDays(-30), 14);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Premium, DateTime.UtcNow.AddDays(-16));

        SetupCheckoutInfrastructure(tid, tenant, sub);

        var handler = CreateHandler(CreateBillingOptions());
        var result = await handler.Handle(new StartSubscriptionCheckoutCommand(tid, "Basic"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.DowngradeMustBeScheduled");
        _checkoutSessionsWrite.Verify(x => x.AddAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnSuccess_When_ExpiredTrialEffectiveReadOnly_RequestsSamePlanRenewal()
    {
        var tid = Guid.NewGuid();
        var tenant = CreateTenant(tid);
        var sub = TenantSubscription.StartTrial(
            tid,
            SubscriptionPlanCode.Basic,
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            trialDays: 0);

        SetupCheckoutInfrastructure(tid, tenant, sub);
        SetupManualCheckoutProvider(10_000L);

        var handler = CreateHandler(CreateBillingOptions());
        var result = await handler.Handle(new StartSubscriptionCheckoutCommand(tid, "Basic"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TargetPlanCode.Should().Be("Basic");
        result.Value.TrialPlanChangeApplied.Should().BeFalse();
        result.Value.CheckoutSessionId.Should().NotBeNull();
        _checkoutSessionsWrite.Verify(x => x.AddAsync(It.IsAny<BillingCheckoutSession>(), It.IsAny<CancellationToken>()), Times.Once);
        _subscriptionsWrite.Verify(x => x.UpdateAsync(It.IsAny<TenantSubscription>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
