using Ardalis.Specification;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Hospitalizations.Specs;

/// <summary>
/// Active = <see cref="Hospitalization.DischargedAtUtc"/> is null. Optionally exclude one row (for updates).
/// </summary>
public sealed class ActiveHospitalizationForPetClinicSpec : Specification<Hospitalization>
{
    public ActiveHospitalizationForPetClinicSpec(Guid tenantId, Guid clinicId, Guid petId, Guid? excludeHospitalizationId)
    {
        Query.Where(x =>
            x.TenantId == tenantId
            && x.ClinicId == clinicId
            && x.PetId == petId
            && x.DischargedAtUtc == null);

        if (excludeHospitalizationId.HasValue)
            Query.Where(x => x.Id != excludeHospitalizationId.Value);
    }
}
