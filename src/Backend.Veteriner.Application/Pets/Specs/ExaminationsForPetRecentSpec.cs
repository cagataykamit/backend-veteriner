using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Application.Common.Specifications;

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
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderByDescending(e => e.ExaminedAtUtc)
            .ThenByDescending(e => e.Id)
            .Take(take);
    }
}
