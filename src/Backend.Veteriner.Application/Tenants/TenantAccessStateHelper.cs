using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Abonelik özeti ile aynı effective status / write izni kurallarını kullanır
/// (<see cref="TenantSubscriptionEffectiveWriteEvaluator"/>).
/// </summary>
public static class TenantAccessStateHelper
{
    public static TenantAccessStateDto BuildForActiveTenant(
        Guid tenantId,
        TenantSubscription subscription,
        DateTime utcNow)
    {
        var effective = TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(subscription, utcNow);
        if (TenantSubscriptionEffectiveWriteEvaluator.WriteAllowed(effective))
        {
            return new TenantAccessStateDto(tenantId, IsReadOnly: false, ReasonCode: null, Message: null);
        }

        return effective switch
        {
            TenantSubscriptionStatus.ReadOnly => new TenantAccessStateDto(
                tenantId,
                IsReadOnly: true,
                ReasonCode: "Subscriptions.TenantReadOnly",
                Message: "Deneme süreniz sona erdi veya abonelik salt okunur modda; yazma işlemleri kapalı."),

            TenantSubscriptionStatus.Cancelled => new TenantAccessStateDto(
                tenantId,
                IsReadOnly: true,
                ReasonCode: "Subscriptions.TenantCancelled",
                Message: "Abonelik iptal edildi; yazma işlemleri kapalı."),

            _ => new TenantAccessStateDto(
                tenantId,
                IsReadOnly: true,
                ReasonCode: "Subscriptions.WriteNotAllowed",
                Message: "Mevcut abonelik durumunda yazma işlemleri yapılamıyor."),
        };
    }
}
