using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Deneme kısıtı kararı (TRIAL-004): abonelik kaydı <see cref="TenantSubscriptionStatus.Trialing"/> ise kiracı
/// kısıta tabidir. Saklanan durum esas alınır: süresi bitmiş deneme (etkin durum ReadOnly) de ücretli değildir,
/// kısıt abonelikle (<c>ActivatePaidPlan</c>) kalkar. Abonelik kaydı yoksa güvenli taraf seçilir (kısıtlı).
/// Scoped: aynı istek içinde kiracı başına tek sorgu.
/// </summary>
public sealed class TrialLimitPolicy : ITrialLimitPolicy
{
    private readonly IReadRepository<TenantSubscription> _subscriptions;
    private readonly Dictionary<Guid, bool> _cache = new();

    public TrialLimitPolicy(IReadRepository<TenantSubscription> subscriptions)
        => _subscriptions = subscriptions;

    /// <summary>Elinde abonelik kaydı olan çağıranlar (arka plan işleyici) için aynı kural.</summary>
    public static bool IsTrial(TenantSubscription subscription)
        => subscription.Status == TenantSubscriptionStatus.Trialing;

    public async Task<bool> IsTrialAsync(Guid tenantId, CancellationToken ct)
    {
        if (_cache.TryGetValue(tenantId, out var cached))
            return cached;

        var subscription = await _subscriptions.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(tenantId), ct);
        var isTrial = subscription is null || IsTrial(subscription);
        _cache[tenantId] = isTrial;
        return isTrial;
    }

    public async Task<ExportRowLimit> GetExportRowLimitAsync(Guid tenantId, CancellationToken ct)
        => ExportRowLimit.For(await IsTrialAsync(tenantId, ct));
}
