using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Examinations.Specs;

/// <summary>
/// Update için izlenmeyen (AsNoTracking) yükleme: kayıt sonra istemcinin beklenen sürümüyle
/// eklenir, böylece UPDATE koşulundaki rowversion istemcinin gönderdiği değerdir.
/// </summary>
public sealed class ExaminationForUpdateByIdSpec : Specification<Examination>
{
    public ExaminationForUpdateByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(e => e.TenantId == tenantId && e.Id == id);
    }
}
