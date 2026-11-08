using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Kiracının paketi özelliği içeriyor mu (TIER-001): tablo <see cref="SubscriptionPlanCatalog"/>, karar burada. Deneme seçilen pakete
/// göre davranır, çok klinik dahil (ADR-035 2026-11-08, TRIAL_LIMITS K7). Paket değişiminde sığma kuralı da buradadır
/// (<see cref="GetPlanFitAsync"/>). Salt okunur durum ayrı kapıdır
/// (<see cref="TenantSubscriptionEffectiveWriteEvaluator"/>). Abonelik kaydı olmayan kiracı kilitli sayılır.
/// Kapsamlı (scoped) servistir; abonelik istek başına kiracı başına bir kez okunur.
/// </summary>
public sealed class TenantPlanFeatureEvaluator
{
    public const string FeatureNotInPlanCode = "Subscriptions.FeatureNotInPlan";
    public const string PlanLimitsExceededCode = "Subscriptions.PlanLimitsExceeded";

    private readonly IReadRepository<TenantSubscription> _subscriptions;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<UserTenant> _userTenants;
    private readonly IReadRepository<TenantInvite> _invites;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<Guid, TenantSubscription?> _subscriptionsByTenant = [];

    public TenantPlanFeatureEvaluator(
        IReadRepository<TenantSubscription> subscriptions,
        IReadRepository<Clinic> clinics,
        IReadRepository<UserTenant> userTenants,
        IReadRepository<TenantInvite> invites,
        TimeProvider timeProvider)
    {
        _subscriptions = subscriptions;
        _clinics = clinics;
        _userTenants = userTenants;
        _invites = invites;
        _timeProvider = timeProvider;
    }

    /// <summary>Paketin aktif klinik sınırı: <see cref="PlanFeature.MultiClinic"/> yoksa 1, varsa sınırsız (<c>null</c>).</summary>
    public static int? MaxActiveClinics(SubscriptionPlanCode plan)
        => SubscriptionPlanCatalog.Allows(plan, PlanFeature.MultiClinic) ? null : 1;

    /// <summary>Abonelik için özellik açık mı; <c>access-state</c> da aynı kuraldan okur. Deneme dahil yalnız paket belirler.</summary>
    public static bool IsEnabled(TenantSubscription subscription, PlanFeature feature)
        => SubscriptionPlanCatalog.Allows(subscription.PlanCode, feature);

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

    /// <summary>Hedef paket kiracının bugünkü aktif klinik ve kullanıcı sayısına sığıyor mu (ADR-035 2026-11-08, seçenek A).</summary>
    public async Task<SubscriptionPlanFit> GetPlanFitAsync(Guid tenantId, SubscriptionPlanCode target, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var activeClinics = await _clinics.CountAsync(new ActiveClinicsByTenantCountSpec(tenantId), ct);
        var members = await _userTenants.CountAsync(new UserTenantsByTenantCountSpec(tenantId), ct);
        var pendingInvites = await _invites.CountAsync(new PendingTenantInvitesByTenantCountSpec(tenantId, now), ct);
        return new SubscriptionPlanFit(
            target, MaxActiveClinics(target), SubscriptionPlanCatalog.GetMaxUsers(target), activeClinics, members, pendingInvites);
    }

    /// <summary>
    /// Paket değişikliği (planlı düşürme, denemede değişim, ücretliye geçiş, dönem sonu uygulama) hedef pakete sığmıyorsa 409
    /// <see cref="PlanLimitsExceededCode"/>; yanıtta sınırlar, bugünkü sayılar ve fazlalar yazar. Veri silinmez.
    /// </summary>
    public async Task<Result> EnsureFitsPlanAsync(Guid tenantId, SubscriptionPlanCode target, CancellationToken ct)
    {
        var fit = await GetPlanFitAsync(tenantId, target, ct);
        return fit.Fits ? Result.Success() : Result.Failure(PlanLimitsExceeded(fit));
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

    private static Error PlanLimitsExceeded(SubscriptionPlanFit fit)
        => new(
            PlanLimitsExceededCode,
            "Seçilen paket mevcut kullanımınıza sığmıyor; önce fazla klinikleri pasife alın ya da kullanıcıları çıkarın.",
            new Dictionary<string, object?>
            {
                ["targetPlanCode"] = SubscriptionPlanCatalog.ToApiCode(fit.TargetPlanCode),
                ["maxActiveClinics"] = fit.MaxActiveClinics,
                ["maxUsers"] = fit.MaxUsers,
                ["activeClinicCount"] = fit.ActiveClinicCount,
                ["memberCount"] = fit.MemberCount,
                ["pendingInviteCount"] = fit.PendingInviteCount,
                ["excessClinicCount"] = fit.ExcessClinicCount,
                ["excessUserCount"] = fit.ExcessUserCount,
            });
}
