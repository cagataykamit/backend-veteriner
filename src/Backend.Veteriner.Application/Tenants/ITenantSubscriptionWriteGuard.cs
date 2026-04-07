using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Kiracı bağlamında yazma (mutation) işlemlerinden önce abonelik + trial süresi ile effective karar.
/// </summary>
public interface ITenantSubscriptionWriteGuard
{
    Task<Result> EnsureWritesAllowedAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
