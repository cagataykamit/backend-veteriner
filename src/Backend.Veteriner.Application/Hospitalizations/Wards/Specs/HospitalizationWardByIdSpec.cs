using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Specs;

public sealed class HospitalizationWardByIdSpec : Specification<HospitalizationWard>
{
    public HospitalizationWardByIdSpec(Guid tenantId, Guid clinicId, Guid wardId)
    {
        Query.Where(x => x.TenantId == tenantId && x.Id == wardId);
        Query.ApplyClinicScope(clinicId, null);
    }
}
