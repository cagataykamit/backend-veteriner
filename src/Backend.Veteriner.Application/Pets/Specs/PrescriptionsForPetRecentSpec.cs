using Ardalis.Specification;
using Backend.Veteriner.Domain.Prescriptions;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PrescriptionsForPetRecentSpec : Specification<Prescription>
{
    public PrescriptionsForPetRecentSpec(Guid tenantId, Guid? clinicId, Guid petId, int take)
    {
        Query.Where(p => p.TenantId == tenantId && p.PetId == petId);
        if (clinicId.HasValue)
            Query.Where(p => p.ClinicId == clinicId.Value);
        Query.OrderByDescending(p => p.PrescribedAtUtc)
            .ThenByDescending(p => p.Id)
            .Take(take);
    }
}
