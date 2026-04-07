using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

public sealed record SubscriptionSeatSnapshot(
    int MemberCount,
    int PendingInviteCount,
    int MaxUsers,
    TenantSubscriptionStatus SubscriptionStatus);
