using Ardalis.Specification;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class LabResultsForPetRecentSpec : Specification<LabResult>
{
    public LabResultsForPetRecentSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        int take,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.Where(x => x.TenantId == tenantId && x.PetId == petId && x.Status != LabResultStatus.Cancelled);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderByDescending(x => x.RequestedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(take);
    }
}
