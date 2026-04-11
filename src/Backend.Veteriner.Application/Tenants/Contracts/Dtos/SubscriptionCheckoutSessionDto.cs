using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

public sealed record SubscriptionCheckoutSessionDto(
    Guid CheckoutSessionId,
    Guid TenantId,
    string CurrentPlanCode,
    string TargetPlanCode,
    BillingCheckoutSessionStatus Status,
    BillingProvider Provider,
    string? CheckoutUrl,
    bool CanContinue,
    DateTime? ExpiresAtUtc,
    string? ChargeCurrencyCode,
    long? ProratedChargeMinor,
    decimal? ProrationRatio);

