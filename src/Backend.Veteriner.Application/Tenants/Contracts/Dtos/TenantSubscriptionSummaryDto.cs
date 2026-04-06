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
    IReadOnlyList<SubscriptionPlanOptionDto> AvailablePlans);

public sealed record SubscriptionPlanOptionDto(string Code, string Name, string? Description);
