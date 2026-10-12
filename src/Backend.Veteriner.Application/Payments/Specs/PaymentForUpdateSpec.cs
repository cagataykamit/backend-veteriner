using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Specs;

/// <summary>İzlenen (tracked) ödeme; iptal ucu için. İptal edilmiş kayıt da gelir (<c>Payments.AlreadyVoided</c> ayrımı için).</summary>
public sealed class PaymentForUpdateSpec : Specification<Payment>
{
    public PaymentForUpdateSpec(Guid tenantId, Guid id)
    {
        Query.IgnoreQueryFilters();
        Query.Where(p => p.TenantId == tenantId && p.Id == id);
    }
}

/// <summary>İzlenen (tracked) aktif tahsisler; ödeme iptalinde geri alınır.</summary>
public sealed class ActivePaymentAllocationsForUpdateSpec : Specification<PaymentAllocation>
{
    public ActivePaymentAllocationsForUpdateSpec(Guid tenantId, Guid paymentId)
    {
        Query.Where(a => a.TenantId == tenantId && a.PaymentId == paymentId);
    }
}
