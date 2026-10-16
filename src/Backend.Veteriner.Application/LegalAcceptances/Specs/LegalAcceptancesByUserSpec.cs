using Ardalis.Specification;
using Backend.Veteriner.Domain.Legal;

namespace Backend.Veteriner.Application.LegalAcceptances.Specs;

public sealed class LegalAcceptancesByUserSpec : Specification<LegalAcceptance>
{
    public LegalAcceptancesByUserSpec(Guid userId)
    {
        Query.Where(x => x.UserId == userId).AsNoTracking();
    }
}
