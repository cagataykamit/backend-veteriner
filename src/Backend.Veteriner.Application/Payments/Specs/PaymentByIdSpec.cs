using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Specs;

public sealed class PaymentByIdSpec : Specification<Payment>
{
    public PaymentByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(p => p.TenantId == tenantId && p.Id == id);
    }
}
