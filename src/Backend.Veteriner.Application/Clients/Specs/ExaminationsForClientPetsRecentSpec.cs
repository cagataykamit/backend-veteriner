using Ardalis.Specification;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Clients.Specs;

public sealed class ExaminationsForClientPetsRecentSpec : Specification<Examination>
{
    public ExaminationsForClientPetsRecentSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid[] petIds,
        int take,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.Where(e => e.TenantId == tenantId);
        DashboardSpecificationClinicScope.ApplyToExamination(Query, clinicId, accessibleClinicIds);
        Query.Where(e => petIds.Contains(e.PetId));
        Query.OrderByDescending(e => e.ExaminedAtUtc)
            .ThenByDescending(e => e.Id)
            .Take(take);
    }
}
