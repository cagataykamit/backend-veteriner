using Backend.Veteriner.Domain.SalesPartners;
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

    /// <summary>Ücretli dönem türü (BILL-001); dönem <see cref="ActivatedAtUtc"/> ile başlar, <see cref="PaidPeriodEndUtc"/> ile biter.</summary>
    public BillingPeriodKind BillingPeriod { get; private set; } = BillingPeriodKind.Monthly;

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

    /// <summary>
    /// Deneme (trial) döneminde plan seçimi/yükseltme: 14 gün ücretsiz vaadi gereği ödeme farkı
    /// hesaplanmaz ve checkout başlatılmaz (Model A). Seçilen plan doğrudan aktif plan olarak
    /// yazılır; trial status, trial tarihleri ve billing period anchor'ı DEĞİŞMEZ. Ücretli akış
    /// yalnızca trial bittiğinde (ör. ActivatePaidPlan ile) devreye girer.
    /// </summary>
    public void ChangePlanDuringTrial(SubscriptionPlanCode targetPlanCode, DateTime utcNow)
    {
        if (Status != TenantSubscriptionStatus.Trialing)
        {
            throw new InvalidOperationException(
                "ChangePlanDuringTrial yalnızca Trialing durumundaki abonelikler için kullanılabilir.");
        }

        var now = NormalizeUtc(utcNow);
        PlanCode = targetPlanCode;
        UpdatedAtUtc = now;
    }

    /// <summary>Ücretli dönemin bitişi; hiç ödeme alınmadıysa <c>null</c>.</summary>
    public DateTime? PaidPeriodEndUtc
        => ActivatedAtUtc is { } start ? BillingPeriods.EndOf(start, BillingPeriod) : null;

    /// <summary>Mevcut dönem türünü koruyarak planı etkinleştirir (yükseltme, planlı düşürme).</summary>
    public void ActivatePaidPlan(SubscriptionPlanCode targetPlanCode, DateTime utcNow)
        => ActivatePaidPlan(targetPlanCode, BillingPeriod, utcNow);

    public void ActivatePaidPlan(SubscriptionPlanCode targetPlanCode, BillingPeriodKind period, DateTime utcNow)
    {
        var now = NormalizeUtc(utcNow);
        var anchor = ActivatedAtUtc;
        var currentPeriodExpired = PaidPeriodEndUtc is { } end && end <= now;
        var periodChanged = period != BillingPeriod;

        if (Status == TenantSubscriptionStatus.Active && PlanCode == targetPlanCode && !periodChanged && !currentPeriodExpired)
        {
            UpdatedAtUtc = now;
            return;
        }

        // Billing cycle anchor korunur: süresi dolmamış aktif abonelikte plan geçişi (upgrade) anchor'ı
        // reset etmez. Önceki dönem dolmuşsa (renewal) ya da dönem türü değiştiyse (aylıktan yıllığa;
        // kalan günler tahsilattan düşülmüştür) yeni dönem ödeme anında başlar.
        PlanCode = targetPlanCode;
        BillingPeriod = period;
        Status = TenantSubscriptionStatus.Active;
        ActivatedAtUtc = currentPeriodExpired || periodChanged || anchor is null ? now : anchor.Value;
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
