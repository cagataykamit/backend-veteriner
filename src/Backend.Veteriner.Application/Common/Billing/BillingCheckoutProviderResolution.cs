using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Common.Billing;

/// <summary>
/// <see cref="BillingOptions.DefaultCheckoutProvider"/> değerinden etkin <see cref="BillingProvider"/> üretir.
/// Auto modunda öncelik Iyzico -> Stripe şeklindedir.
/// Eksik yapılandırmada sessizce Manual'e düşmez; açık hata döner.
/// </summary>
public static class BillingCheckoutProviderResolution
{
    public static Result<BillingProvider> Resolve(BillingOptions options)
    {
        var raw = options.DefaultCheckoutProvider?.Trim() ?? "";

        if (string.IsNullOrEmpty(raw) || raw.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            if (IyzicoCheckoutPrerequisites.IsComplete(options.Iyzico))
                return Result<BillingProvider>.Success(BillingProvider.Iyzico);

            if (StripeCheckoutPrerequisites.IsComplete(options.Stripe))
                return Result<BillingProvider>.Success(BillingProvider.Stripe);

            return Result<BillingProvider>.Failure(
                "Billing.ProviderConfigurationIncomplete",
                "DefaultCheckoutProvider Auto (veya boş) ancak Iyzico/Stripe yapılandırması eksik. Iyzico için ApiKey/SecretKey/BaseUrl/CallbackUrl/PlanPricesTry veya Stripe için SecretKey/SuccessUrl/CancelUrl/SubscriptionPriceIds doldurun; yalnızca manuel/test akışı istiyorsanız DefaultCheckoutProvider değerini Manual yapın.");
        }

        return raw.ToUpperInvariant() switch
        {
            "MANUAL" => Result<BillingProvider>.Success(BillingProvider.Manual),
            "STRIPE" => ResolveStripeWhenExplicit(options.Stripe),
            "IYZICO" or "İYZİCO" => ResolveIyzicoWhenExplicit(options.Iyzico),
            _ => Result<BillingProvider>.Failure(
                "Billing.InvalidCheckoutProvider",
                $"Geçersiz Billing:DefaultCheckoutProvider değeri: '{raw}'. İzin verilenler: Manual, Stripe, Iyzico, Auto."),
        };
    }

    private static Result<BillingProvider> ResolveStripeWhenExplicit(StripeBillingOptions stripe)
    {
        if (StripeCheckoutPrerequisites.IsComplete(stripe))
            return Result<BillingProvider>.Success(BillingProvider.Stripe);

        return Result<BillingProvider>.Failure(
            "Billing.StripeConfigurationIncomplete",
            "DefaultCheckoutProvider Stripe ancak Stripe yapılandırması eksik. Billing:Stripe:SecretKey, SuccessUrl, CancelUrl ve katalogdaki her plan için SubscriptionPriceIds girişlerini doldurun.");
    }

    private static Result<BillingProvider> ResolveIyzicoWhenExplicit(IyzicoBillingOptions iyzico)
    {
        if (IyzicoCheckoutPrerequisites.IsComplete(iyzico))
            return Result<BillingProvider>.Success(BillingProvider.Iyzico);

        return Result<BillingProvider>.Failure(
            "Billing.IyzicoConfigurationIncomplete",
            "DefaultCheckoutProvider Iyzico ancak yapılandırma eksik. Billing:Iyzico:ApiKey, SecretKey, BaseUrl, CallbackUrl ve PlanPricesTry altında katalogdaki her plan için fiyat girişlerini doldurun.");
    }
}
