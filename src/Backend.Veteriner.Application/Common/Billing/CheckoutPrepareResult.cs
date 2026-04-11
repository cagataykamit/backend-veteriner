namespace Backend.Veteriner.Application.Common.Billing;

public sealed record CheckoutPrepareResult(
    string? CheckoutUrl,
    string? ExternalReference,
    string? ChargeCurrencyCode = null,
    long? ChargeAmountMinor = null,
    decimal? ProrationRatio = null);
