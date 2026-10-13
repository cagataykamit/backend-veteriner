using Ardalis.Specification;
using Backend.Veteriner.Domain.CareInstructions;

namespace Backend.Veteriner.Application.CareInstructions.Specs;

/// <summary>
/// Update için izlenmeyen (AsNoTracking) yükleme: kayıt sonra istemcinin beklenen sürümüyle
/// eklenir, böylece UPDATE koşulundaki rowversion istemcinin gönderdiği değerdir.
/// </summary>
public sealed class CareInstructionForUpdateByIdSpec : Specification<CareInstruction>
{
    public CareInstructionForUpdateByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.Id == id);
    }
}
