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
    IReadOnlyList<SubscriptionPlanOptionDto> AvailablePlans);

public sealed record SubscriptionPlanOptionDto(string Code, string Name, string? Description, int MaxUsers);

public sealed record PendingSubscriptionPlanChangeDto(
    Guid Id,
    string CurrentPlanCode,
    string TargetPlanCode,
    SubscriptionPlanChangeType ChangeType,
    SubscriptionPlanChangeStatus Status,
    DateTime RequestedAtUtc,
    DateTime EffectiveAtUtc,
    string? Reason);
