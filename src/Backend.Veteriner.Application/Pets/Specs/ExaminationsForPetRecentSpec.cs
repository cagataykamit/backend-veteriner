using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class ExaminationsForPetRecentSpec : Specification<Examination>
{
    public ExaminationsForPetRecentSpec(Guid tenantId, Guid? clinicId, Guid petId, int take)
    {
        Query.Where(e => e.TenantId == tenantId && e.PetId == petId);
        if (clinicId.HasValue)
            Query.Where(e => e.ClinicId == clinicId.Value);
        Query.OrderByDescending(e => e.ExaminedAtUtc)
            .ThenByDescending(e => e.Id)
            .Take(take);
    }
}
