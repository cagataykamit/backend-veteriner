using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Koltuk/limit hesabı: <see cref="UserTenants"/> satır sayısı + süresi dolmamış bekleyen davet sayısı.
/// Plan limiti <see cref="SubscriptionPlanCatalog.MaxUsers"/> ile katalogdan okunur.
/// </summary>
public sealed class TenantSubscriptionSeatEvaluator
{
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<TenantSubscription> _subscriptions;
    private readonly IReadRepository<UserTenant> _userTenants;
    private readonly IReadRepository<TenantInvite> _invites;

    public TenantSubscriptionSeatEvaluator(
        IReadRepository<Tenant> tenants,
        IReadRepository<TenantSubscription> subscriptions,
        IReadRepository<UserTenant> userTenants,
        IReadRepository<TenantInvite> invites)
    {
        _tenants = tenants;
        _subscriptions = subscriptions;
        _userTenants = userTenants;
        _invites = invites;
    }

    public static bool SubscriptionAllowsInvites(TenantSubscriptionStatus status)
        => status is TenantSubscriptionStatus.Trialing or TenantSubscriptionStatus.Active;

    public async Task<Result<SubscriptionSeatSnapshot>> TryBuildAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<SubscriptionSeatSnapshot>.Failure("Tenants.NotFound", "Tenant bulunamadı.");
        if (!tenant.IsActive)
        {
            return Result<SubscriptionSeatSnapshot>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için bu işlem yapılamaz.");
        }

        var sub = await _subscriptions.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(tenantId), ct);
        if (sub is null)
        {
            return Result<SubscriptionSeatSnapshot>.Failure(
                "Subscriptions.NotFound",
                "Bu kiracı için abonelik kaydı bulunamadı.");
        }

        if (sub.Status == TenantSubscriptionStatus.ReadOnly)
        {
            return Result<SubscriptionSeatSnapshot>.Failure(
                "Subscriptions.SubscriptionReadOnly",
                "Abonelik salt okunur; davet oluşturulamaz veya kabul edilemez.");
        }

        if (sub.Status == TenantSubscriptionStatus.Cancelled)
        {
            return Result<SubscriptionSeatSnapshot>.Failure(
                "Subscriptions.SubscriptionCancelled",
                "Abonelik iptal edilmiş; davet oluşturulamaz veya kabul edilemez.");
        }

        if (!SubscriptionAllowsInvites(sub.Status))
        {
            return Result<SubscriptionSeatSnapshot>.Failure(
                "Subscriptions.InvitesNotAllowed",
                "Mevcut abonelik durumunda davet desteklenmiyor.");
        }

        var maxUsers = SubscriptionPlanCatalog.GetMaxUsers(sub.PlanCode);
        var utcNow = DateTime.UtcNow;

        var memberCount = await _userTenants.CountAsync(new UserTenantsByTenantCountSpec(tenantId), ct);
        var pendingCount = await _invites.CountAsync(new PendingTenantInvitesByTenantCountSpec(tenantId, utcNow), ct);

        return Result<SubscriptionSeatSnapshot>.Success(
            new SubscriptionSeatSnapshot(memberCount, pendingCount, maxUsers, sub.Status));
    }
}
