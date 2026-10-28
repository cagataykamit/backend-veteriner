using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

/// <summary>Sağlayıcıda iadesi bekleyen çift ödemeler (BILL-005), en eski tespit önce.</summary>
public sealed class PendingRefundCheckoutSessionsSpec : Specification<BillingCheckoutSession>
{
    public PendingRefundCheckoutSessionsSpec(int take)
    {
        Query.Where(x => x.RefundStatus == BillingCheckoutRefundStatus.Pending)
            .OrderBy(x => x.DuplicatePaymentDetectedAtUtc)
            .Take(take);
    }
}
