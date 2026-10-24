using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Specs;

/// <summary>
/// Aktif bölümler arasında ad eşleşmesi (case-insensitive); <paramref name="normalizedNameLowerInvariant"/>
/// handler'da <c>Trim().ToLowerInvariant()</c> ile üretilir. Veritabanı karşılığı: filtreli unique indeks (IsActive = 1).
/// </summary>
public sealed class ActiveHospitalizationWardByClinicAndNameSpec : Specification<HospitalizationWard>
{
    public ActiveHospitalizationWardByClinicAndNameSpec(
        Guid tenantId,
        Guid clinicId,
        string normalizedNameLowerInvariant,
        Guid? excludeWardId = null)
    {
        Query.Where(x => x.TenantId == tenantId && x.IsActive && x.Name.ToLower() == normalizedNameLowerInvariant);
        Query.ApplyClinicScope(clinicId, null);
        if (excludeWardId.HasValue)
            Query.Where(x => x.Id != excludeWardId.Value);
    }
}
