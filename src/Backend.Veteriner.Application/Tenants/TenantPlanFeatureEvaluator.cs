using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Kiracının paketi özelliği içeriyor mu (TIER-001): kural <see cref="SubscriptionPlanCatalog"/>, karar burada. Abonelik durumuna
/// bakmaz: deneme seçilen pakete göre davranır, salt okunur durum ayrı kapıdır
/// (<see cref="TenantSubscriptionEffectiveWriteEvaluator"/>). Abonelik kaydı olmayan kiracı kilitli sayılır.
/// Kapsamlı (scoped) servistir; abonelik istek başına kiracı başına bir kez okunur.
/// </summary>
public sealed class TenantPlanFeatureEvaluator
{
    public const string FeatureNotInPlanCode = "Subscriptions.FeatureNotInPlan";

    private readonly IReadRepository<TenantSubscription> _subscriptions;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly Dictionary<Guid, SubscriptionPlanCode?> _plans = [];

    public TenantPlanFeatureEvaluator(IReadRepository<TenantSubscription> subscriptions, IReadRepository<Clinic> clinics)
    {
        _subscriptions = subscriptions;
        _clinics = clinics;
    }

    public async Task<bool> IsAllowedAsync(Guid tenantId, PlanFeature feature, CancellationToken ct)
        => await GetPlanAsync(tenantId, ct) is { } plan && SubscriptionPlanCatalog.Allows(plan, feature);

    /// <summary>Paket özelliği içermiyorsa 403 <see cref="FeatureNotInPlanCode"/>; yanıtta istemci için gereken paket yazar.</summary>
    public async Task<Result> EnsureAllowedAsync(Guid tenantId, PlanFeature feature, CancellationToken ct)
    {
        var plan = await GetPlanAsync(tenantId, ct);
        return plan is { } code && SubscriptionPlanCatalog.Allows(code, feature)
            ? Result.Success()
            : Result.Failure(NotInPlan(feature, plan));
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

    private async Task<SubscriptionPlanCode?> GetPlanAsync(Guid tenantId, CancellationToken ct)
    {
        if (_plans.TryGetValue(tenantId, out var cached))
            return cached;

        var sub = await _subscriptions.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(tenantId), ct);
        return _plans[tenantId] = sub?.PlanCode;
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
