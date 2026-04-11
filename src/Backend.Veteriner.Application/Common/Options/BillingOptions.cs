namespace Backend.Veteriner.Application.Common.Options;

public sealed class BillingOptions
{
    /// <summary>Manual, Stripe veya Iyzico (büyük/küçük harf duyarsız).</summary>
    public string DefaultCheckoutProvider { get; init; } = "Manual";

    /// <summary>
    /// Plan API kodu (Basic, Pro, Premium) -> fiyat (minor unit, TRY için kurus).
    /// Proration hesapları provider-agnostic bu değerler üzerinden yapılır.
    /// </summary>
    public Dictionary<string, long> PlanPricesMinor { get; init; } = new();

    /// <summary>Plan fiyat sözlüğü para birimi (ISO-4217 alpha-3).</summary>
    public string PlanPriceCurrency { get; init; } = "TRY";

    public StripeBillingOptions Stripe { get; init; } = new();

    public IyzicoBillingOptions Iyzico { get; init; } = new();
}

public sealed class StripeBillingOptions
{
    /// <summary>Stripe API secret key (sk_test_... / sk_live_...). Checkout Session oluşturmak için gerekli.</summary>
    public string SecretKey { get; init; } = "";

    public string WebhookSecret { get; init; } = "";

    /// <summary>
    /// Ödeme sonrası yönlendirme URL’si. Stripe <c>{CHECKOUT_SESSION_ID}</c> yer tutucusunu oturum id’si ile değiştirir.
    /// </summary>
    public string SuccessUrl { get; init; } = "";

    /// <summary>Kullanıcı iptal ettiğinde dönüş URL’si (genelde abonelik / ödeme sayfası).</summary>
    public string CancelUrl { get; init; } = "";

    /// <summary>
    /// Plan API kodu (Basic, Pro, Premium) → Stripe recurring <c>price_...</c> id.
    /// Para birimi ve tutar Stripe Price kaydında tanımlıdır.
    /// </summary>
    public Dictionary<string, string> SubscriptionPriceIds { get; init; } = new();
}

public sealed class IyzicoBillingOptions
{
    /// <summary>Iyzico API key (x-iyzi-apiKey).</summary>
    public string ApiKey { get; init; } = "";

    /// <summary>Iyzico secret key (request auth + webhook verify).</summary>
    public string SecretKey { get; init; } = "";

    /// <summary>Iyzico merchant id (özellikle subscription webhook doğrulaması için).</summary>
    public string MerchantId { get; init; } = "";

    /// <summary>Sandbox: https://sandbox-api.iyzipay.com, production: https://api.iyzipay.com</summary>
    public string BaseUrl { get; init; } = "";

    /// <summary>CF initialize callback URL (Iyzico'nun success/fail post ettiği merchant URL).</summary>
    public string CallbackUrl { get; init; } = "";

    /// <summary>Backend callback sonrası frontend success yönlendirme adresi.</summary>
    public string ReturnSuccessUrl { get; init; } = "";

    /// <summary>Backend callback sonrası frontend fail/cancel yönlendirme adresi.</summary>
    public string ReturnFailureUrl { get; init; } = "";

    /// <summary>
    /// Plan API kodu (Basic, Pro, Premium) → tek seferlik checkout tutarı (TRY).
    /// Subscription planlama altyapısı oturana kadar checkout-form omurgası için kullanılır.
    /// </summary>
    public Dictionary<string, decimal> PlanPricesTry { get; init; } = new();

    /// <summary>Webhook V3 imzası için güvenlik paylaşımlı anahtar (Iyzico secret).</summary>
    public string WebhookSecret { get; init; } = "";

    /// <summary>
    /// Yalnızca Development + sandbox BaseUrl + imza başlığı yokken webhook’u geçici kabul eder.
    /// Production’da kapalı tutulmalıdır (varsayılan false).
    /// </summary>
    public bool AllowUnsignedSandboxWebhooks { get; init; }

    /// <summary>Checkout form locale: tr veya en.</summary>
    public string Locale { get; init; } = "tr";

    /// <summary>Fatura/teslimat adresinde kullanılacak varsayılan ülke.</summary>
    public string Country { get; init; } = "Turkey";

    /// <summary>Fatura/teslimat adresinde kullanılacak varsayılan şehir.</summary>
    public string City { get; init; } = "Istanbul";
}
