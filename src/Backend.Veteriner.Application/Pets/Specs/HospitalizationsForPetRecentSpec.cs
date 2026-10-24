using Ardalis.Specification;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class HospitalizationsForPetRecentSpec : Specification<Hospitalization>
{
    public HospitalizationsForPetRecentSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        int take,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.Where(x => x.TenantId == tenantId && x.PetId == petId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderByDescending(x => x.AdmittedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(take);
    }
}
