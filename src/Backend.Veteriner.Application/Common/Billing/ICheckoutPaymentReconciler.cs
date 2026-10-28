using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Common.Billing;

/// <summary>
/// Açık checkout oturumunu sağlayıcıyla uzlaştırır: ödeme sayfasının durumunu sorar, ödeme alınmışsa aboneliği
/// <see cref="BillingActivationSource.ProviderVerified"/> ile etkinleştirir. Oturum kapatılmadan ya da yeniden
/// kullanılmadan önce ve kullanıcı sağlayıcıdan döndüğünde çağrılır.
/// </summary>
public interface ICheckoutPaymentReconciler
{
    /// <param name="tenantIdConstraint">Panel isteğinde kiracı; sağlayıcı dönüşünde (kiracısız) <c>null</c>.</param>
    Task<CheckoutPageState> ReconcileAsync(BillingCheckoutSession session, Guid? tenantIdConstraint, CancellationToken ct = default);
}
