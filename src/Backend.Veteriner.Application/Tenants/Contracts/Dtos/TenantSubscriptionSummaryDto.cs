using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

public sealed record TenantSubscriptionSummaryDto(
    Guid TenantId,
    string TenantName,
    string PlanCode,
    string PlanName,
    TenantSubscriptionStatus Status,
    DateTime? TrialStartsAtUtc,
    DateTime? TrialEndsAtUtc,
    int? DaysRemaining,
    bool IsReadOnly,
    bool CanManageSubscription,
    DateTime CurrentPeriodStartUtc,
    DateTime CurrentPeriodEndUtc,
    DateTime BillingCycleAnchorUtc,
    DateTime NextBillingAtUtc,
    PendingSubscriptionPlanChangeDto? PendingPlanChange,
    IReadOnlyList<SubscriptionPlanOptionDto> AvailablePlans,
    string BillingPeriod = "Monthly",
    PendingSubscriptionPlanChangeDto? RejectedPlanChange = null);

/// <summary>
/// Hedef paketin sınırları ve bugünkü kullanım (ADR-035 2026-11-08, seçenek A); <c>Subscriptions.PlanLimitsExceeded</c> yanıtıyla aynı
/// alanlar. <c>MaxActiveClinics</c> <c>null</c> ise klinik sınırı yok. Kullanıcı sayımı üye + bekleyen davettir.
/// </summary>
public sealed record SubscriptionPlanFitDto(
    string TargetPlanCode,
    int? MaxActiveClinics,
    int MaxUsers,
    int ActiveClinicCount,
    int MemberCount,
    int PendingInviteCount,
    int ExcessClinicCount,
    int ExcessUserCount)
{
    public static SubscriptionPlanFitDto From(SubscriptionPlanFit fit)
        => new(
            SubscriptionPlanCatalog.ToApiCode(fit.TargetPlanCode),
            fit.MaxActiveClinics,
            fit.MaxUsers,
            fit.ActiveClinicCount,
            fit.MemberCount,
            fit.PendingInviteCount,
            fit.ExcessClinicCount,
            fit.ExcessUserCount);
}

/// <summary>Fiyatlar kuruş, KDV hariç; checkout'un okuduğu kuraldan (WEB-007). Yapılandırılmamışsa <c>null</c>.</summary>
public sealed record SubscriptionPlanOptionDto(
    string Code,
    string Name,
    string? Description,
    int MaxUsers,
    long? MonthlyPriceMinor = null,
    long? YearlyPriceMinor = null);

public sealed record PendingSubscriptionPlanChangeDto(
    Guid Id,
    string CurrentPlanCode,
    string TargetPlanCode,
    SubscriptionPlanChangeType ChangeType,
    SubscriptionPlanChangeStatus Status,
    DateTime RequestedAtUtc,
    DateTime EffectiveAtUtc,
    string? Reason,
    SubscriptionPlanFitDto? Fit = null);
