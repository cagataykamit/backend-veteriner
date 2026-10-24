using Ardalis.Specification;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PrescriptionsForPetRecentSpec : Specification<Prescription>
{
    public PrescriptionsForPetRecentSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        int take,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.Where(p => p.TenantId == tenantId && p.PetId == petId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderByDescending(p => p.PrescribedAtUtc)
            .ThenByDescending(p => p.Id)
            .Take(take);
    }
}
