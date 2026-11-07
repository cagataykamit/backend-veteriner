using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Tenants;

/// <summary>TIER-001: paket → özellik tablosu ve kiracı için karar (docs/PLAN_FEATURES_API_CONTRACT.md).</summary>
public sealed class TenantPlanFeatureEvaluatorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Theory]
    [InlineData(SubscriptionPlanCode.Basic, PlanFeature.OwnerPortal, false)]
    [InlineData(SubscriptionPlanCode.Basic, PlanFeature.PortalAppointmentRequests, false)]
    [InlineData(SubscriptionPlanCode.Basic, PlanFeature.MultiClinic, false)]
    [InlineData(SubscriptionPlanCode.Pro, PlanFeature.OwnerPortal, true)]
    [InlineData(SubscriptionPlanCode.Pro, PlanFeature.PortalAppointmentRequests, true)]
    [InlineData(SubscriptionPlanCode.Pro, PlanFeature.MultiClinic, false)]
    [InlineData(SubscriptionPlanCode.Premium, PlanFeature.OwnerPortal, true)]
    [InlineData(SubscriptionPlanCode.Premium, PlanFeature.PortalAppointmentRequests, true)]
    [InlineData(SubscriptionPlanCode.Premium, PlanFeature.MultiClinic, true)]
    public void Catalog_Should_Match_Adr035_Table(SubscriptionPlanCode plan, PlanFeature feature, bool expected)
        => SubscriptionPlanCatalog.Allows(plan, feature).Should().Be(expected);

    [Theory]
    [InlineData(PlanFeature.OwnerPortal, SubscriptionPlanCode.Pro)]
    [InlineData(PlanFeature.PortalAppointmentRequests, SubscriptionPlanCode.Pro)]
    [InlineData(PlanFeature.MultiClinic, SubscriptionPlanCode.Premium)]
    public void MinimumPlanFor_Should_Return_Smallest_Plan(PlanFeature feature, SubscriptionPlanCode expected)
        => SubscriptionPlanCatalog.MinimumPlanFor(feature).Should().Be(expected);

    [Fact]
    public async Task EnsureAllowed_Should_Fail_With_RequiredPlan_When_Feature_Not_In_Plan()
    {
        var result = await PlanFeatureTestFactory.ForPlan(SubscriptionPlanCode.Basic)
            .EnsureAllowedAsync(TenantId, PlanFeature.OwnerPortal, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(TenantPlanFeatureEvaluator.FeatureNotInPlanCode);
        result.Error.Extensions!["feature"].Should().Be("OwnerPortal");
        result.Error.Extensions!["currentPlanCode"].Should().Be("Basic");
        result.Error.Extensions!["requiredPlanCode"].Should().Be("Pro");
    }

    [Fact]
    public async Task Should_Treat_Tenant_Without_Subscription_As_Locked()
    {
        var evaluator = PlanFeatureTestFactory.ForPlan(null);

        (await evaluator.IsAllowedAsync(TenantId, PlanFeature.OwnerPortal, CancellationToken.None)).Should().BeFalse();
        var result = await evaluator.EnsureAllowedAsync(TenantId, PlanFeature.MultiClinic, CancellationToken.None);
        result.Error.Extensions!["currentPlanCode"].Should().BeNull();
    }

    [Fact]
    public async Task EnsureCanAddActiveClinic_Should_Allow_First_Clinic_On_Basic()
    {
        var result = await PlanFeatureTestFactory.ForPlan(SubscriptionPlanCode.Basic, activeClinic: null)
            .EnsureCanAddActiveClinicAsync(TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(SubscriptionPlanCode.Basic)]
    [InlineData(SubscriptionPlanCode.Pro)]
    public async Task EnsureCanAddActiveClinic_Should_Reject_Second_Clinic_Without_MultiClinic(SubscriptionPlanCode plan)
    {
        var result = await PlanFeatureTestFactory.ForPlan(plan, new Clinic(TenantId, "Merkez", "İzmir"))
            .EnsureCanAddActiveClinicAsync(TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Extensions!["requiredPlanCode"].Should().Be("Premium");
    }

    [Fact]
    public async Task EnsureCanAddActiveClinic_Should_Allow_Second_Clinic_On_Premium()
    {
        var result = await PlanFeatureTestFactory.ForPlan(SubscriptionPlanCode.Premium, new Clinic(TenantId, "Merkez", "İzmir"))
            .EnsureCanAddActiveClinicAsync(TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
