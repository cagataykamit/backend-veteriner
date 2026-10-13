using Ardalis.Specification;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Hospitalizations.Specs;

/// <summary>
/// Update/taburcu için izlenmeyen (AsNoTracking) yükleme: kayıt sonra istemcinin beklenen sürümüyle
/// eklenir, böylece UPDATE koşulundaki rowversion istemcinin gönderdiği değerdir.
/// </summary>
public sealed class HospitalizationForUpdateByIdSpec : Specification<Hospitalization>
{
    public HospitalizationForUpdateByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.Id == id);
    }
}
