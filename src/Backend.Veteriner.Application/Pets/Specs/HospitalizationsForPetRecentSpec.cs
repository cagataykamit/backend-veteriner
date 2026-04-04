using Ardalis.Specification;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class HospitalizationsForPetRecentSpec : Specification<Hospitalization>
{
    public HospitalizationsForPetRecentSpec(Guid tenantId, Guid? clinicId, Guid petId, int take)
    {
        Query.Where(x => x.TenantId == tenantId && x.PetId == petId);
        if (clinicId.HasValue)
            Query.Where(x => x.ClinicId == clinicId.Value);
        Query.OrderByDescending(x => x.AdmittedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(take);
    }
}
