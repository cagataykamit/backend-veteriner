using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PaymentsForPetRecentSpec : Specification<Payment>
{
    public PaymentsForPetRecentSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        int take,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.Where(p => p.TenantId == tenantId && p.PetId == petId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderByDescending(p => p.PaidAtUtc)
            .ThenByDescending(p => p.Id)
            .Take(take);
    }
}
