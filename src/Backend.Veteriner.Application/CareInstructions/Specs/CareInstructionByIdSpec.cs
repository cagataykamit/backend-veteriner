using Ardalis.Specification;
using Backend.Veteriner.Domain.CareInstructions;

namespace Backend.Veteriner.Application.CareInstructions.Specs;

public sealed class CareInstructionByIdSpec : Specification<CareInstruction>
{
    public CareInstructionByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(x => x.TenantId == tenantId && x.Id == id);
    }
}
