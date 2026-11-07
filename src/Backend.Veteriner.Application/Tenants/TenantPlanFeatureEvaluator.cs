using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Kiracının paketi özelliği içeriyor mu (TIER-001): tablo <see cref="SubscriptionPlanCatalog"/>, karar burada. Deneme seçilen pakete
/// göre davranır; tek istisna çok kliniktir (TRIAL_LIMITS K7: denemede şube sayısı sınırlanmaz). Salt okunur durum ayrı kapıdır
/// (<see cref="TenantSubscriptionEffectiveWriteEvaluator"/>). Abonelik kaydı olmayan kiracı kilitli sayılır.
/// Kapsamlı (scoped) servistir; abonelik istek başına kiracı başına bir kez okunur.
/// </summary>
public sealed class TenantPlanFeatureEvaluator
{
    public const string FeatureNotInPlanCode = "Subscriptions.FeatureNotInPlan";

    private readonly IReadRepository<TenantSubscription> _subscriptions;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly Dictionary<Guid, TenantSubscription?> _subscriptionsByTenant = [];

    public TenantPlanFeatureEvaluator(IReadRepository<TenantSubscription> subscriptions, IReadRepository<Clinic> clinics)
    {
        _subscriptions = subscriptions;
        _clinics = clinics;
    }

    /// <summary>Abonelik için özellik açık mı; <c>access-state</c> da aynı kuraldan okur.</summary>
    public static bool IsEnabled(TenantSubscription subscription, PlanFeature feature)
        => (feature == PlanFeature.MultiClinic && subscription.Status == TenantSubscriptionStatus.Trialing)
           || SubscriptionPlanCatalog.Allows(subscription.PlanCode, feature);

    public async Task<bool> IsAllowedAsync(Guid tenantId, PlanFeature feature, CancellationToken ct)
        => await GetSubscriptionAsync(tenantId, ct) is { } subscription && IsEnabled(subscription, feature);

    /// <summary>Paket özelliği içermiyorsa 403 <see cref="FeatureNotInPlanCode"/>; yanıtta istemci için gereken paket yazar.</summary>
    public async Task<Result> EnsureAllowedAsync(Guid tenantId, PlanFeature feature, CancellationToken ct)
    {
        var subscription = await GetSubscriptionAsync(tenantId, ct);
        return subscription is not null && IsEnabled(subscription, feature)
            ? Result.Success()
            : Result.Failure(NotInPlan(feature, subscription?.PlanCode));
    }

    /// <summary>
    /// Kiracıya bir aktif klinik daha eklenebilir mi (yeni klinik, pasif kliniği açma). <see cref="PlanFeature.MultiClinic"/> yoksa
    /// en fazla bir aktif klinik; pasif klinik sayılmaz.
    /// </summary>
    public async Task<Result> EnsureCanAddActiveClinicAsync(Guid tenantId, CancellationToken ct)
    {
        var allowed = await EnsureAllowedAsync(tenantId, PlanFeature.MultiClinic, ct);
        if (allowed.IsSuccess)
            return allowed;

        return await _clinics.FirstOrDefaultAsync(new ActiveClinicsByTenantTakeSpec(tenantId, 1), ct) is null
            ? Result.Success()
            : allowed;
    }

    private async Task<TenantSubscription?> GetSubscriptionAsync(Guid tenantId, CancellationToken ct)
    {
        if (_subscriptionsByTenant.TryGetValue(tenantId, out var cached))
            return cached;

        var subscription = await _subscriptions.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(tenantId), ct);
        return _subscriptionsByTenant[tenantId] = subscription;
    }

    private static Error NotInPlan(PlanFeature feature, SubscriptionPlanCode? current)
        => new(
            FeatureNotInPlanCode,
            "Bu özellik mevcut paketinizde yok; üst pakete geçerek kullanabilirsiniz.",
            new Dictionary<string, object?>
            {
                ["feature"] = feature.ToString(),
                ["currentPlanCode"] = current is { } code ? SubscriptionPlanCatalog.ToApiCode(code) : null,
                ["requiredPlanCode"] = SubscriptionPlanCatalog.ToApiCode(SubscriptionPlanCatalog.MinimumPlanFor(feature)),
            });
}
