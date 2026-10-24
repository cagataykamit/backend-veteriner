using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Specs;

/// <summary>Oda; <paramref name="wardId"/> verilirse yalnız o bölümün odası.</summary>
public sealed class HospitalizationRoomByIdSpec : Specification<HospitalizationRoom>
{
    public HospitalizationRoomByIdSpec(Guid tenantId, Guid clinicId, Guid roomId, Guid? wardId = null)
    {
        Query.Where(x => x.TenantId == tenantId && x.Id == roomId);
        Query.ApplyClinicScope(clinicId, null);
        if (wardId.HasValue)
            Query.Where(x => x.WardId == wardId.Value);
    }
}
