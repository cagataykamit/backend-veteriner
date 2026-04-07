using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Saklanan <see cref="TenantSubscriptionStatus"/> ile <see cref="TenantSubscription.TrialEndsAtUtc"/> birlikte
/// değerlendirilerek kiracıda mutation (yazma) işlemlerine izin verilip verilmeyeceğini hesaplar.
/// </summary>
public static class TenantSubscriptionEffectiveWriteEvaluator
{
    public static bool AllowsTenantMutations(TenantSubscription sub, DateTime utcNow)
        => AllowsTenantMutations(sub.Status, sub.TrialEndsAtUtc, utcNow);

    /// <summary>
    /// Test ve özet DTO üretimi için parametreli sürüm.
    /// </summary>
    public static bool AllowsTenantMutations(
        TenantSubscriptionStatus status,
        DateTime? trialEndsAtUtc,
        DateTime utcNow)
    {
        return status switch
        {
            TenantSubscriptionStatus.Cancelled => false,
            TenantSubscriptionStatus.ReadOnly => false,
            TenantSubscriptionStatus.Active => true,
            TenantSubscriptionStatus.Trialing =>
                !(trialEndsAtUtc is { } end && utcNow >= end),
            _ => false,
        };
    }
}
