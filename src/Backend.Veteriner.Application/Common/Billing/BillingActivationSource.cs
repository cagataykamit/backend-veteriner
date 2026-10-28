namespace Backend.Veteriner.Application.Common.Billing;

public enum BillingActivationSource
{
    Manual = 0,
    Webhook = 1,

    /// <summary>
    /// Backend ödeme durumunu sağlayıcıya kendi gizli anahtarıyla sordu ve ödemenin alındığını gördü (iyzico CheckoutForm
    /// retrieve). Webhook kaybolursa (ör. sunucu o an ulaşılamazken) ödeme boşa gitmesin diye webhook'a ek doğrulama yolu.
    /// </summary>
    ProviderVerified = 2,
}
