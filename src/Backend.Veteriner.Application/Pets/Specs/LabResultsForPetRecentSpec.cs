using Ardalis.Specification;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Domain.LabResults;

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
        Query.Where(x => x.TenantId == tenantId && x.PetId == petId);
        DashboardSpecificationClinicScope.ApplyToLabResult(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(x => x.ResultDateUtc)
            .ThenByDescending(x => x.Id)
            .Take(take);
    }
}
