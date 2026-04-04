using Ardalis.Specification;
using Backend.Veteriner.Domain.Treatments;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class TreatmentsForPetRecentSpec : Specification<Treatment>
{
    public TreatmentsForPetRecentSpec(Guid tenantId, Guid? clinicId, Guid petId, int take)
    {
        Query.Where(t => t.TenantId == tenantId && t.PetId == petId);
        if (clinicId.HasValue)
            Query.Where(t => t.ClinicId == clinicId.Value);
        Query.OrderByDescending(t => t.TreatmentDateUtc)
            .ThenByDescending(t => t.Id)
            .Take(take);
    }
}
