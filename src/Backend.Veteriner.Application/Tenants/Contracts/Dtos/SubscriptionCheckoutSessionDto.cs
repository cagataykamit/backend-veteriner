using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Checkout başlatma/sorgulama sonucu. Trial dönemindeki plan değişikliklerinde (Model A / Seçenek A)
/// hiçbir ödeme çağrılmadığından ve <see cref="BillingCheckoutSession"/> oluşturulmadığından,
/// checkout'a özgü alanlar (CheckoutSessionId/Status/Provider/CheckoutUrl) null döner ve
/// <see cref="TrialPlanChangeApplied"/> true olur. Bu durumda frontend ödeme akışı beklememelidir.
/// </summary>
public sealed record SubscriptionCheckoutSessionDto(
    Guid? CheckoutSessionId,
    Guid TenantId,
    string CurrentPlanCode,
    string TargetPlanCode,
    BillingCheckoutSessionStatus? Status,
    BillingProvider? Provider,
    string? CheckoutUrl,
    bool CanContinue,
    DateTime? ExpiresAtUtc,
    string? ChargeCurrencyCode,
    long? ProratedChargeMinor,
    decimal? ProrationRatio,
    bool TrialPlanChangeApplied);

