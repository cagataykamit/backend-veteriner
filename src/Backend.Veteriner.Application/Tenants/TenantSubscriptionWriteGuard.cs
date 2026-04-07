using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

public sealed class TenantSubscriptionWriteGuard : ITenantSubscriptionWriteGuard
{
    private readonly IReadRepository<TenantSubscription> _subscriptions;

    public TenantSubscriptionWriteGuard(IReadRepository<TenantSubscription> subscriptions)
    {
        _subscriptions = subscriptions;
    }

    public async Task<Result> EnsureWritesAllowedAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var sub = await _subscriptions.FirstOrDefaultAsync(
            new TenantSubscriptionByTenantIdSpec(tenantId), cancellationToken);
        if (sub is null)
        {
            return Result.Failure(
                "Subscriptions.NotFound",
                "Bu kiracı için abonelik kaydı bulunamadı; yazma işlemi yapılamaz.");
        }

        var utcNow = DateTime.UtcNow;
        if (!TenantSubscriptionEffectiveWriteEvaluator.AllowsTenantMutations(sub, utcNow))
        {
            return Result.Failure(
                "Subscriptions.TenantReadOnly",
                "Abonelik deneme süresi sona ermiş veya salt okunur durumdadır; yazma işlemi yapılamaz. Aboneliği sürdürmek için yöneticinize başvurun.");
        }

        return Result.Success();
    }
}
