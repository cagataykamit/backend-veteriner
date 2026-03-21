using Ardalis.Specification;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Vaccinations.Specs;

public sealed class VaccinationByIdSpec : Specification<Vaccination>
{
    public VaccinationByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(v => v.TenantId == tenantId && v.Id == id);
    }
}
