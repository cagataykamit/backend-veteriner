using Ardalis.Specification;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Hospitalizations.Specs;

/// <summary>
/// Update/taburcu için izlenen yükleme: istemcinin beklediği sürüm <c>SetExpectedRowVersion</c> ile yazılır ve kaydetmede
/// UPDATE koşuluna taşınır (<c>ExpectedRowVersionSaveChangesInterceptor</c>); özgün değerler korunduğu için değişiklik geçmişi alan farkını yazar.
/// </summary>
public sealed class HospitalizationForUpdateByIdSpec : Specification<Hospitalization>
{
    public HospitalizationForUpdateByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(x => x.TenantId == tenantId && x.Id == id);
    }
}
