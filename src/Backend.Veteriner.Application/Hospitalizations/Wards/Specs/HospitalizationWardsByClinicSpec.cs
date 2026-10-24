using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Specs;

/// <summary>Kliniğin tüm bölümleri (aktif/pasif), <c>SortOrder</c> sonra ad sıralı.</summary>
public sealed class HospitalizationWardsByClinicSpec : Specification<HospitalizationWard>
{
    public HospitalizationWardsByClinicSpec(Guid tenantId, Guid clinicId)
    {
        Query.Where(x => x.TenantId == tenantId);
        Query.ApplyClinicScope(clinicId, null);
        Query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name);
    }
}
