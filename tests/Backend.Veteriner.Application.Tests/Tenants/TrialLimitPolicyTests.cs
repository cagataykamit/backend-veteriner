using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants;

/// <summary>TRIAL-004: deneme kısıtı kararı ve dışa aktarma satır tavanı (TRIAL_LIMITS_API_CONTRACT.md).</summary>
public sealed class TrialLimitPolicyTests
{
    private readonly Mock<IReadRepository<TenantSubscription>> _subscriptions = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private TrialLimitPolicy Policy() => new(_subscriptions.Object);

    private void SetupSubscription(TenantSubscription? subscription)
        => _subscriptions
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

    [Fact]
    public async Task IsTrial_Should_BeTrue_When_Subscription_Is_Trialing()
    {
        SetupSubscription(TenantSubscription.StartTrial(_tenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));

        (await Policy().IsTrialAsync(_tenantId, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task IsTrial_Should_StayTrue_When_Trial_Expired_But_Not_Paid()
    {
        // Süresi bitmiş deneme etkin durumda ReadOnly'dir; ücretli değildir, kısıt abonelikle kalkar.
        SetupSubscription(TenantSubscription.StartTrial(_tenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow.AddDays(-40), 14));

        (await Policy().IsTrialAsync(_tenantId, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task IsTrial_Should_BeFalse_When_Subscription_Is_Active()
    {
        var now = DateTime.UtcNow;
        var sub = TenantSubscription.StartTrial(_tenantId, SubscriptionPlanCode.Pro, now.AddDays(-10), 14);
        sub.ActivatePaidPlan(SubscriptionPlanCode.Pro, now);
        SetupSubscription(sub);

        (await Policy().IsTrialAsync(_tenantId, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task IsTrial_Should_BeTrue_When_Subscription_Missing()
    {
        SetupSubscription(null);

        (await Policy().IsTrialAsync(_tenantId, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task IsTrial_Should_Query_Once_Per_Tenant_Within_Scope()
    {
        SetupSubscription(TenantSubscription.StartTrial(_tenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var policy = Policy();

        await policy.IsTrialAsync(_tenantId, CancellationToken.None);
        await policy.GetExportRowLimitAsync(_tenantId, CancellationToken.None);
        await policy.IsTrialAsync(_tenantId, CancellationToken.None);

        _subscriptions.Verify(
            x => x.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetExportRowLimit_Should_Be_1000_For_Trial_And_SharedLimit_For_Paid()
    {
        SetupSubscription(TenantSubscription.StartTrial(_tenantId, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var trial = await Policy().GetExportRowLimitAsync(_tenantId, CancellationToken.None);

        var now = DateTime.UtcNow;
        var paidSub = TenantSubscription.StartTrial(_tenantId, SubscriptionPlanCode.Basic, now.AddDays(-1), 14);
        paidSub.ActivatePaidPlan(SubscriptionPlanCode.Basic, now);
        SetupSubscription(paidSub);
        var paid = await Policy().GetExportRowLimitAsync(_tenantId, CancellationToken.None);

        trial.Should().Be(new ExportRowLimit(1_000, IsTrial: true));
        trial.MaxRows.Should().Be(TrialLimits.ExportMaxRows);
        paid.Should().Be(new ExportRowLimit(ReportsSharedLimits.MaxExportRows, IsTrial: false));
        paid.MaxRows.Should().Be(50_000);
    }

    [Fact]
    public void ExportRowLimit_Ensure_Should_Return_TrialCode_Only_For_Trial()
    {
        var trialExceeded = ExportRowLimit.Trial.Ensure(TrialLimits.ExportMaxRows + 1, "Payments.ReportExportTooManyRows");
        var trialWithin = ExportRowLimit.Trial.Ensure(TrialLimits.ExportMaxRows, "Payments.ReportExportTooManyRows");
        var paidWithin = ExportRowLimit.Paid.Ensure(TrialLimits.ExportMaxRows + 1, "Payments.ReportExportTooManyRows");
        var paidExceeded = ExportRowLimit.Paid.Ensure(ReportsSharedLimits.MaxExportRows + 1, "Payments.ReportExportTooManyRows");

        trialExceeded.IsSuccess.Should().BeFalse();
        trialExceeded.Error.Code.Should().Be("Trial.ExportLimitExceeded");
        trialExceeded.Error.Message.Should().Be(
            "Deneme sürümünde dışa aktarma en fazla 1000 satır sınırı; abonelikle kalkar. Filtreyi daraltın.");
        trialWithin.IsSuccess.Should().BeTrue();
        paidWithin.IsSuccess.Should().BeTrue();
        paidExceeded.IsSuccess.Should().BeFalse();
        paidExceeded.Error.Code.Should().Be("Payments.ReportExportTooManyRows");
        paidExceeded.Error.Message.Should().Be("Dışa aktarma en fazla 50000 satır destekler; filtreyi daraltın.");
    }
}
