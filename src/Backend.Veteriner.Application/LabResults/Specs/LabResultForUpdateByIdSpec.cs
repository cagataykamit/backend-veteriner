using Ardalis.Specification;
using Backend.Veteriner.Domain.LabResults;

namespace Backend.Veteriner.Application.LabResults.Specs;

/// <summary>
/// Update/iptal için izlenen yükleme: istemcinin beklediği sürüm <c>SetExpectedRowVersion</c> ile yazılır ve kaydetmede
/// UPDATE koşuluna taşınır (<c>ExpectedRowVersionSaveChangesInterceptor</c>); özgün değerler korunduğu için değişiklik geçmişi alan farkını yazar.
/// </summary>
public sealed class LabResultForUpdateByIdSpec : Specification<LabResult>
{
    public LabResultForUpdateByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(x => x.TenantId == tenantId && x.Id == id)
            .Include(x => x.Lines);
    }
}
