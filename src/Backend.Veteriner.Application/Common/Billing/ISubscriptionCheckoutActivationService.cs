using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Common.Billing;

public interface ISubscriptionCheckoutActivationService
{
    /// <summary>
    /// Checkout session'ı hedef plana göre Active yapar; tekrar çağrılarda idempotent davranır.
    /// <paramref name="tenantIdConstraint"/> doluysa session bu kiracıya ait olmalı (panel finalize).
    /// Webhook için null geçilir; session yalnızca id ile yüklenir.
    /// <paramref name="providerMustMatch"/> doluysa session oluşturulurken seçilen ödeme sağlayıcı ile eşleşmeli (webhook güvenliği).
    /// <see cref="BillingActivationSource.Manual"/> (panel finalize) yalnız Manual sağlayıcı oturumunu etkinleştirir;
    /// Iyzico/Stripe oturumları <see cref="BillingActivationSource.Webhook"/> (imzası doğrulanmış) ya da
    /// <see cref="BillingActivationSource.ProviderVerified"/> (backend sağlayıcıya sordu, ödeme alınmış) ile etkinleşir.
    /// </summary>
    Task<Result<SubscriptionCheckoutSessionDto>> TryActivateAsync(
        Guid checkoutSessionId,
        Guid? tenantIdConstraint,
        BillingProvider? providerMustMatch,
        string? externalReference,
        BillingActivationSource source,
        CancellationToken ct = default);
}
