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
    /// <summary>Ücretli (Active) abonelik; <paramref name="trialing"/> ise deneme.</summary>
    public static TenantPlanFeatureEvaluator ForPlan(SubscriptionPlanCode? plan, Clinic? activeClinic = null, bool trialing = false)
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
        return new TenantPlanFeatureEvaluator(subscriptions.Object, clinics.Object);
    }
}
