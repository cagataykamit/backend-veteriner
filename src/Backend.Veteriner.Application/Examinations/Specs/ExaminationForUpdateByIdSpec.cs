using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Examinations.Specs;

/// <summary>
/// Update için izlenen yükleme: istemcinin beklediği sürüm <c>SetExpectedRowVersion</c> ile yazılır ve kaydetmede
/// UPDATE koşuluna taşınır (<c>ExpectedRowVersionSaveChangesInterceptor</c>); özgün değerler korunduğu için değişiklik geçmişi alan farkını yazar.
/// </summary>
public sealed class ExaminationForUpdateByIdSpec : Specification<Examination>
{
    public ExaminationForUpdateByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(e => e.TenantId == tenantId && e.Id == id);
    }
}
