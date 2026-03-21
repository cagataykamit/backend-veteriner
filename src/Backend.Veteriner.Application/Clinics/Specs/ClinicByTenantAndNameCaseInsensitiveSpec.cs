using Ardalis.Specification;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Specs;

/// <summary>
/// Aynı kiracı altında klinik adı eşleşmesi (case-insensitive).
/// <paramref name="normalizedNameLowerInvariant"/> handler'da <c>Trim().ToLowerInvariant()</c> ile üretilmelidir.
/// </summary>
public sealed class ClinicByTenantAndNameCaseInsensitiveSpec : Specification<Clinic>
{
    public ClinicByTenantAndNameCaseInsensitiveSpec(Guid tenantId, string normalizedNameLowerInvariant)
    {
        Query.Where(c => c.TenantId == tenantId && c.Name.ToLower() == normalizedNameLowerInvariant);
    }
}
