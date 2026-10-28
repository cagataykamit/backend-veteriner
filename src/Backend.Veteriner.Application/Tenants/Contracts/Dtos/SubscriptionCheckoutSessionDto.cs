using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Checkout başlatma/sorgulama sonucu. Trial dönemindeki plan değişikliklerinde (Model A / Seçenek A)
/// hiçbir ödeme çağrılmadığından ve <see cref="BillingCheckoutSession"/> oluşturulmadığından,
/// checkout'a özgü alanlar (CheckoutSessionId/Status/Provider/CheckoutUrl) null döner ve
/// <see cref="TrialPlanChangeApplied"/> true olur. Bu durumda frontend ödeme akışı beklememelidir.
/// <see cref="ProratedChargeMinor"/> çekilecek (satış ortağı kodu indirimi sonrası) tutardır; <see cref="DiscountAmountMinor"/>
/// uygulanan kod indirimidir, yoksa <c>null</c> (ADR-037).
/// <see cref="RefundStatus"/> oturumun ödemesi çift ödeme sayılıp iadeye alındıysa doludur (<c>Pending</c>, <c>Refunded</c>,
/// <c>ManualRequired</c>; BILL-005), aksi halde <c>null</c>.
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
    bool TrialPlanChangeApplied,
    long? DiscountAmountMinor = null,
    string BillingPeriod = "Monthly",
    string? RefundStatus = null);

