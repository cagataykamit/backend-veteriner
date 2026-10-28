using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

/// <summary>
/// Çift ödeme olarak işaretlenmiş oturumlar (platform yöneticisi listesi): elle iade bekleyenler önce (sınırda kaybolmasın),
/// sonra en yeni tespit.
/// </summary>
public sealed class DuplicatePaymentCheckoutSessionsSpec : Specification<BillingCheckoutSession>
{
    public DuplicatePaymentCheckoutSessionsSpec(int take)
    {
        Query.AsNoTracking();
        Query.Where(x => x.RefundStatus != BillingCheckoutRefundStatus.None)
            .OrderByDescending(x => x.RefundStatus == BillingCheckoutRefundStatus.ManualRequired)
            .ThenByDescending(x => x.DuplicatePaymentDetectedAtUtc)
            .Take(take);
    }
}
