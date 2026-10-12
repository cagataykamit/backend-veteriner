using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.Specs;

public sealed class PaymentByRequestIdSpec : Specification<Payment>
{
    public PaymentByRequestIdSpec(Guid tenantId, Guid requestId)
    {
        Query.AsNoTracking();
        // İptal edilmiş kayıt da bulunmalı: yoksa aynı requestId benzersiz indekse çarpar (RequestIdVoided).
        Query.IgnoreQueryFilters();
        Query.Where(p => p.TenantId == tenantId && p.RequestId == requestId);
    }
}
