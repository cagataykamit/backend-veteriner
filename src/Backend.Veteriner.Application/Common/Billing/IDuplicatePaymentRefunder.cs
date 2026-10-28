using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Common.Billing;

/// <summary>
/// Çift ödemeyi sağlayıcıda geri verir ve sonucu oturuma yazar (BILL-005). Geçici hatada yeniden denenir; denemeler
/// tükenir ya da sağlayıcı otomatik iadeyi desteklemezse platform yöneticisine elle iade olarak düşer.
/// </summary>
public interface IDuplicatePaymentRefunder
{
    /// <returns>İade sağlayıcıda yapıldıysa <c>true</c>.</returns>
    Task<bool> RefundAsync(BillingCheckoutSession session, CancellationToken ct = default);
}
