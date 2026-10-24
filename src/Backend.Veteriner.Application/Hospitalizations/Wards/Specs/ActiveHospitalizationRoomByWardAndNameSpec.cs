using Ardalis.Specification;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Specs;

/// <summary>
/// Aynı bölümün aktif odaları arasında ad eşleşmesi (case-insensitive). Veritabanı karşılığı: filtreli unique indeks.
/// </summary>
public sealed class ActiveHospitalizationRoomByWardAndNameSpec : Specification<HospitalizationRoom>
{
    public ActiveHospitalizationRoomByWardAndNameSpec(
        Guid tenantId,
        Guid wardId,
        string normalizedNameLowerInvariant,
        Guid? excludeRoomId = null)
    {
        Query.Where(x =>
            x.TenantId == tenantId
            && x.WardId == wardId
            && x.IsActive
            && x.Name.ToLower() == normalizedNameLowerInvariant);
        if (excludeRoomId.HasValue)
            Query.Where(x => x.Id != excludeRoomId.Value);
    }
}
