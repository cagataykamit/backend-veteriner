using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Trial tarihi + saklanan status üzerinden "effective" subscription durumunu hesaplar
/// ve write işlemlerinin izinli olup olmadığını merkezi şekilde değerlendirir.
/// </summary>
public sealed class TenantSubscriptionEffectiveWriteEvaluator
{
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<TenantSubscription> _subscriptions;

    public TenantSubscriptionEffectiveWriteEvaluator(
        IReadRepository<Tenant> tenants,
        IReadRepository<TenantSubscription> subscriptions)
    {
        _tenants = tenants;
        _subscriptions = subscriptions;
    }

    public static TenantSubscriptionStatus GetEffectiveStatus(TenantSubscription sub, DateTime utcNow)
    {
        if (sub.Status == TenantSubscriptionStatus.Trialing
            && sub.TrialEndsAtUtc.HasValue
            && sub.TrialEndsAtUtc.Value <= utcNow)
        {
            return TenantSubscriptionStatus.ReadOnly;
        }

        return sub.Status;
    }

    public static bool WriteAllowed(TenantSubscriptionStatus effectiveStatus)
        => effectiveStatus is TenantSubscriptionStatus.Trialing or TenantSubscriptionStatus.Active;

    public async Task<Result> EnsureWriteAllowedAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Result.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için bu işlem yapılamaz.");
        }

        var sub = await _subscriptions.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(tenantId), ct);
        if (sub is null)
        {
            return Result.Failure(
                "Subscriptions.NotFound",
                "Bu kiracı için abonelik kaydı bulunamadı.");
        }

        var effectiveStatus = GetEffectiveStatus(sub, DateTime.UtcNow);
        if (effectiveStatus == TenantSubscriptionStatus.ReadOnly)
        {
            return Result.Failure(
                "Subscriptions.TenantReadOnly",
                "Trial süresi bitmiş veya abonelik salt okunur; yazma işlemleri engellendi.");
        }

        if (effectiveStatus == TenantSubscriptionStatus.Cancelled)
        {
            return Result.Failure(
                "Subscriptions.TenantCancelled",
                "Abonelik iptal edilmiş; yazma işlemleri engellendi.");
        }

        if (!WriteAllowed(effectiveStatus))
        {
            return Result.Failure(
                "Subscriptions.WriteNotAllowed",
                "Mevcut abonelik durumunda yazma işlemleri desteklenmiyor.");
        }

        return Result.Success();
    }
}

