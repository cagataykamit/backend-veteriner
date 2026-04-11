using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Common.Billing;

/// <summary>Provider ham payload'ından çıkarılmış normalize olay.</summary>
public sealed record BillingWebhookNormalizedEvent(
    string ProviderEventId,
    string? EventType,
    BillingWebhookEventKind Kind,
    Guid? BillingCheckoutSessionId,
    string? ProviderPaymentReference);
