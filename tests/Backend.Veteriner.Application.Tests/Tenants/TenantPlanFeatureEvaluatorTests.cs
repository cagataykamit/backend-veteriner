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
    [InlineData(SubscriptionPlanCode.Basic, PlanFeature.AutomatedReminders, false)]
    [InlineData(SubscriptionPlanCode.Pro, PlanFeature.AutomatedReminders, true)]
    [InlineData(SubscriptionPlanCode.Premium, PlanFeature.AutomatedReminders, true)]
    [InlineData(SubscriptionPlanCode.Basic, PlanFeature.AdvancedReports, false)]
    [InlineData(SubscriptionPlanCode.Pro, PlanFeature.AdvancedReports, true)]
    [InlineData(SubscriptionPlanCode.Premium, PlanFeature.AdvancedReports, true)]
    [InlineData(SubscriptionPlanCode.Basic, PlanFeature.VaccinationWizard, false)]
    [InlineData(SubscriptionPlanCode.Pro, PlanFeature.VaccinationWizard, true)]
    [InlineData(SubscriptionPlanCode.Premium, PlanFeature.VaccinationWizard, true)]
    [InlineData(SubscriptionPlanCode.Basic, PlanFeature.MobileBarcode, false)]
    [InlineData(SubscriptionPlanCode.Pro, PlanFeature.MobileBarcode, true)]
    [InlineData(SubscriptionPlanCode.Premium, PlanFeature.MobileBarcode, true)]
    [InlineData(SubscriptionPlanCode.Basic, PlanFeature.ClinicsOverview, false)]
    [InlineData(SubscriptionPlanCode.Pro, PlanFeature.ClinicsOverview, false)]
    [InlineData(SubscriptionPlanCode.Premium, PlanFeature.ClinicsOverview, true)]
    [InlineData(SubscriptionPlanCode.Basic, PlanFeature.CashManagement, false)]
    [InlineData(SubscriptionPlanCode.Pro, PlanFeature.CashManagement, false)]
    [InlineData(SubscriptionPlanCode.Premium, PlanFeature.CashManagement, true)]
    [InlineData(SubscriptionPlanCode.Basic, PlanFeature.ExternalApi, false)]
    [InlineData(SubscriptionPlanCode.Pro, PlanFeature.ExternalApi, false)]
    [InlineData(SubscriptionPlanCode.Premium, PlanFeature.ExternalApi, true)]
    [InlineData(SubscriptionPlanCode.Basic, PlanFeature.CustomDevelopment, false)]
    [InlineData(SubscriptionPlanCode.Pro, PlanFeature.CustomDevelopment, false)]
    [InlineData(SubscriptionPlanCode.Premium, PlanFeature.CustomDevelopment, true)]
    [InlineData(SubscriptionPlanCode.Basic, PlanFeature.Waitlist, false)]
    [InlineData(SubscriptionPlanCode.Pro, PlanFeature.Waitlist, true)]
    [InlineData(SubscriptionPlanCode.Premium, PlanFeature.Waitlist, true)]
    public void Catalog_Should_Match_Adr035_Table(SubscriptionPlanCode plan, PlanFeature feature, bool expected)
        => SubscriptionPlanCatalog.Allows(plan, feature).Should().Be(expected);

    [Theory]
    [InlineData(PlanFeature.OwnerPortal, SubscriptionPlanCode.Pro)]
    [InlineData(PlanFeature.PortalAppointmentRequests, SubscriptionPlanCode.Pro)]
    [InlineData(PlanFeature.MultiClinic, SubscriptionPlanCode.Premium)]
    [InlineData(PlanFeature.ClinicsOverview, SubscriptionPlanCode.Premium)]
    [InlineData(PlanFeature.CashManagement, SubscriptionPlanCode.Premium)]
    [InlineData(PlanFeature.ExternalApi, SubscriptionPlanCode.Premium)]
    [InlineData(PlanFeature.CustomDevelopment, SubscriptionPlanCode.Premium)]
    [InlineData(PlanFeature.Waitlist, SubscriptionPlanCode.Pro)]
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

    /// <summary>ADR-035 2026-11-08: deneme seçilen pakete göre davranır, çok klinik dahil (K7 istisnası kalktı).</summary>
    [Theory]
    [InlineData(SubscriptionPlanCode.Basic, false)]
    [InlineData(SubscriptionPlanCode.Pro, false)]
    [InlineData(SubscriptionPlanCode.Premium, true)]
    public async Task Trial_Should_Follow_Selected_Plan_For_Second_Clinic(SubscriptionPlanCode plan, bool allowed)
    {
        var evaluator = PlanFeatureTestFactory.ForPlan(plan, new Clinic(TenantId, "Merkez", "İzmir"), trialing: true);

        (await evaluator.EnsureCanAddActiveClinicAsync(TenantId, CancellationToken.None)).IsSuccess.Should().Be(allowed);
        (await evaluator.IsAllowedAsync(TenantId, PlanFeature.MultiClinic, CancellationToken.None)).Should().Be(allowed);
    }

    [Fact]
    public async Task Trial_Should_Follow_Selected_Plan_For_Portal()
    {
        var evaluator = PlanFeatureTestFactory.ForPlan(SubscriptionPlanCode.Basic, trialing: true);

        (await evaluator.IsAllowedAsync(TenantId, PlanFeature.OwnerPortal, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task EnsureFitsPlan_Should_Report_Limits_Counts_And_Excess_When_Usage_Does_Not_Fit()
    {
        var result = await PlanFeatureTestFactory.ForPlan(SubscriptionPlanCode.Premium, activeClinicCount: 2, memberCount: 5)
            .EnsureFitsPlanAsync(TenantId, SubscriptionPlanCode.Basic, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(TenantPlanFeatureEvaluator.PlanLimitsExceededCode);
        result.Error.Extensions!["targetPlanCode"].Should().Be("Basic");
        result.Error.Extensions!["maxActiveClinics"].Should().Be(1);
        result.Error.Extensions!["maxUsers"].Should().Be(3);
        result.Error.Extensions!["activeClinicCount"].Should().Be(2);
        result.Error.Extensions!["memberCount"].Should().Be(5);
        result.Error.Extensions!["pendingInviteCount"].Should().Be(0);
        result.Error.Extensions!["excessClinicCount"].Should().Be(1);
        result.Error.Extensions!["excessUserCount"].Should().Be(2);
    }

    [Fact]
    public async Task EnsureFitsPlan_Should_Count_Pending_Invites_Like_Invite_Limit()
    {
        var result = await PlanFeatureTestFactory.ForPlan(SubscriptionPlanCode.Pro, activeClinicCount: 1, memberCount: 3, pendingInviteCount: 1)
            .EnsureFitsPlanAsync(TenantId, SubscriptionPlanCode.Basic, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Extensions!["excessClinicCount"].Should().Be(0);
        result.Error.Extensions!["excessUserCount"].Should().Be(1);
    }

    [Theory]
    [InlineData(SubscriptionPlanCode.Basic, 1, 3)]
    [InlineData(SubscriptionPlanCode.Pro, 1, 5)]
    [InlineData(SubscriptionPlanCode.Premium, 7, 15)]
    public async Task EnsureFitsPlan_Should_Accept_Usage_Within_Target_Limits(SubscriptionPlanCode target, int clinics, int members)
    {
        var result = await PlanFeatureTestFactory.ForPlan(SubscriptionPlanCode.Premium, activeClinicCount: clinics, memberCount: members)
            .EnsureFitsPlanAsync(TenantId, target, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureCanAddActiveClinic_Should_Allow_Second_Clinic_On_Premium()
    {
        var result = await PlanFeatureTestFactory.ForPlan(SubscriptionPlanCode.Premium, new Clinic(TenantId, "Merkez", "İzmir"))
            .EnsureCanAddActiveClinicAsync(TenantId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
