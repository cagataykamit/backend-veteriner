using Ardalis.Specification;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Visits.Specs;

public sealed class VisitsByIdsSpec : Specification<Visit>
{
    public VisitsByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId && ids.Contains(v.Id));
    }
}
