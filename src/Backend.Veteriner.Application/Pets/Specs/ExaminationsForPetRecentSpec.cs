using Ardalis.Specification;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class ExaminationsForPetRecentSpec : Specification<Examination>
{
    public ExaminationsForPetRecentSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        int take,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.Where(e => e.TenantId == tenantId && e.PetId == petId);
        DashboardSpecificationClinicScope.ApplyToExamination(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(e => e.ExaminedAtUtc)
            .ThenByDescending(e => e.Id)
            .Take(take);
    }
}
