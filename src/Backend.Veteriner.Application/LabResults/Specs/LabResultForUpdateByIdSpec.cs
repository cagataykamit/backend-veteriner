using Ardalis.Specification;
using Backend.Veteriner.Domain.LabResults;

namespace Backend.Veteriner.Application.LabResults.Specs;

/// <summary>
/// Update/iptal için izlenmeyen (AsNoTracking) yükleme: kayıt sonra istemcinin beklenen sürümüyle
/// eklenir, böylece UPDATE koşulundaki rowversion istemcinin gönderdiği değerdir.
/// </summary>
public sealed class LabResultForUpdateByIdSpec : Specification<LabResult>
{
    public LabResultForUpdateByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.Id == id);
    }
}
