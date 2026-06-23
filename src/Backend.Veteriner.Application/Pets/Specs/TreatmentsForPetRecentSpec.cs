using Ardalis.Specification;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Domain.Treatments;

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
        DashboardSpecificationClinicScope.ApplyToTreatment(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(t => t.TreatmentDateUtc)
            .ThenByDescending(t => t.Id)
            .Take(take);
    }
}
