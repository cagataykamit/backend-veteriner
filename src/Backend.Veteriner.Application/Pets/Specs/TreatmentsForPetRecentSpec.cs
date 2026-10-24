using Ardalis.Specification;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class TreatmentsForPetRecentSpec : Specification<Treatment>
{
    public TreatmentsForPetRecentSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        int take,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.Where(t => t.TenantId == tenantId && t.PetId == petId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderByDescending(t => t.TreatmentDateUtc)
            .ThenByDescending(t => t.Id)
            .Take(take);
    }
}
