using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Payments.Specs;

/// <summary>Müşterinin klinik kapsamındaki tahsissiz kalan (eski) ödemeleri; en yeni önce.</summary>
public sealed class UnallocatedPaymentsForClientSpec : Specification<Payment>
{
    public UnallocatedPaymentsForClientSpec(
        Guid tenantId,
        Guid clientId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && p.ClientId == clientId && p.AllocatedAmount < p.Amount);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderByDescending(p => p.PaidAtUtc).ThenByDescending(p => p.Id);
    }
}
