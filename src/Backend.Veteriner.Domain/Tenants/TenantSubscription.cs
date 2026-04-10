using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Tenants;

/// <summary>
/// Kiracı başına tek abonelik kaydı (1:1 TenantId). Ödeme sağlayıcısı yok; Faz 1 plan + trial omurgası.
/// </summary>
public sealed class TenantSubscription : AggregateRoot
{
    public Guid TenantId { get; private set; }
    public SubscriptionPlanCode PlanCode { get; private set; }
    public TenantSubscriptionStatus Status { get; private set; }
    public DateTime? TrialStartsAtUtc { get; private set; }
    public DateTime? TrialEndsAtUtc { get; private set; }
    public DateTime? ActivatedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private TenantSubscription() { }

    /// <summary>Yeni kiracı için deneme süreci ile başlatır (varsayılan plan Basic).</summary>
    public static TenantSubscription StartTrial(
        Guid tenantId,
        SubscriptionPlanCode planCode,
        DateTime utcNow,
        int trialDays)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (trialDays < 0)
            throw new ArgumentException("trialDays negatif olamaz.", nameof(trialDays));

        var start = NormalizeUtc(utcNow);
        var end = start.AddDays(trialDays);

        return new TenantSubscription
        {
            TenantId = tenantId,
            PlanCode = planCode,
            Status = TenantSubscriptionStatus.Trialing,
            TrialStartsAtUtc = start,
            TrialEndsAtUtc = end,
            ActivatedAtUtc = null,
            CancelledAtUtc = null,
            CreatedAtUtc = start,
            UpdatedAtUtc = null,
        };
    }

    public void ActivatePaidPlan(SubscriptionPlanCode targetPlanCode, DateTime utcNow)
    {
        var now = NormalizeUtc(utcNow);
        PlanCode = targetPlanCode;
        Status = TenantSubscriptionStatus.Active;
        ActivatedAtUtc = now;
        CancelledAtUtc = null;
        UpdatedAtUtc = now;
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
