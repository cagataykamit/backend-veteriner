using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants;

/// <summary>Sahte depolarla paket kuralı; handler testleri kuralın gerçek kodundan geçer.</summary>
internal static class PlanFeatureTestFactory
{
    /// <summary>Ücretli (Active) abonelik; <paramref name="trialing"/> ise deneme. Sayılar paket sığma kontrolü içindir.</summary>
    public static TenantPlanFeatureEvaluator ForPlan(
        SubscriptionPlanCode? plan,
        Clinic? activeClinic = null,
        bool trialing = false,
        int activeClinicCount = 0,
        int memberCount = 0,
        int pendingInviteCount = 0)
    {
        TenantSubscription? subscription = null;
        if (plan is { } code)
        {
            subscription = TenantSubscription.StartTrial(Guid.NewGuid(), code, DateTime.UtcNow, 14);
            if (!trialing)
                subscription.ActivatePaidPlan(code, DateTime.UtcNow);
        }

        var subscriptions = new Mock<IReadRepository<TenantSubscription>>();
        subscriptions
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantSubscription>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        var clinics = new Mock<IReadRepository<Clinic>>();
        clinics
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeClinic);
        clinics
            .Setup(r => r.CountAsync(It.IsAny<ISpecification<Clinic>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeClinicCount);
        var userTenants = new Mock<IReadRepository<UserTenant>>();
        userTenants
            .Setup(r => r.CountAsync(It.IsAny<ISpecification<UserTenant>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberCount);
        var invites = new Mock<IReadRepository<TenantInvite>>();
        invites
            .Setup(r => r.CountAsync(It.IsAny<ISpecification<TenantInvite>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingInviteCount);
        return new TenantPlanFeatureEvaluator(subscriptions.Object, clinics.Object, userTenants.Object, invites.Object, TimeProvider.System);
    }
}
