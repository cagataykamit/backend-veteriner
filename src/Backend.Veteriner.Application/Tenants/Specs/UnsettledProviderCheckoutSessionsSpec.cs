using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

/// <summary>
/// Sağlayıcıyla uzlaştırılacak oturumlar: belirli bir andan sonra açılmış, sağlayıcı sayfası olan (Manual değil) ve henüz
/// tamamlanmamış. Süresi dolmuş, iptal ya da başarısız işaretlenmiş oturum da dahildir: ödeme sağlayıcıda alınmış olabilir.
/// Çift ödeme olarak işaretlenmiş oturum hariç: onun ödemesi iade sürecindedir (BILL-005).
/// </summary>
public sealed class UnsettledProviderCheckoutSessionsSpec : Specification<BillingCheckoutSession>
{
    public UnsettledProviderCheckoutSessionsSpec(DateTime createdSinceUtc, int take)
    {
        Query.Where(x =>
                x.Provider != BillingProvider.Manual
                && x.Status != BillingCheckoutSessionStatus.Completed
                && x.RefundStatus == BillingCheckoutRefundStatus.None
                && x.ExternalReference != null
                && x.CreatedAtUtc >= createdSinceUtc)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(take);
    }
}
