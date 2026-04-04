using Ardalis.Specification;
using Backend.Veteriner.Domain.LabResults;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class LabResultsForPetRecentSpec : Specification<LabResult>
{
    public LabResultsForPetRecentSpec(Guid tenantId, Guid? clinicId, Guid petId, int take)
    {
        Query.Where(x => x.TenantId == tenantId && x.PetId == petId);
        if (clinicId.HasValue)
            Query.Where(x => x.ClinicId == clinicId.Value);
        Query.OrderByDescending(x => x.ResultDateUtc)
            .ThenByDescending(x => x.Id)
            .Take(take);
    }
}
