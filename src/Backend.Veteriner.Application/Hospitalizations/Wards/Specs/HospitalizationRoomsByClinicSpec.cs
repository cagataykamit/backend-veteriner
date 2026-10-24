using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Specs;

/// <summary>Kliniğin tüm odaları (aktif/pasif), <c>SortOrder</c> sonra ad sıralı; bölüme göre gruplama handler'da.</summary>
public sealed class HospitalizationRoomsByClinicSpec : Specification<HospitalizationRoom>
{
    public HospitalizationRoomsByClinicSpec(Guid tenantId, Guid clinicId)
    {
        Query.Where(x => x.TenantId == tenantId);
        Query.ApplyClinicScope(clinicId, null);
        Query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name);
    }
}
