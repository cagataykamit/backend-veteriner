using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Specs;

public sealed class PaymentsByIdsSpec : Specification<Payment>
{
    public PaymentsByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && ids.Contains(p.Id));
    }
}
