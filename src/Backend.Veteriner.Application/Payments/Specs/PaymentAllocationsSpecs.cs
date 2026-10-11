using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Specs;

public sealed class PaymentAllocationsByPaymentIdSpec : Specification<PaymentAllocation>
{
    public PaymentAllocationsByPaymentIdSpec(Guid tenantId, Guid paymentId)
    {
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId && a.PaymentId == paymentId);
        Query.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id);
    }
}

public sealed class PaymentAllocationsByChargeIdSpec : Specification<PaymentAllocation>
{
    public PaymentAllocationsByChargeIdSpec(Guid tenantId, Guid chargeId)
    {
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId && a.ChargeId == chargeId);
        Query.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id);
    }
}
