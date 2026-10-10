using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Visits.Specs;

public sealed class ExaminationsByVisitIdSpec : Specification<Examination>
{
    public ExaminationsByVisitIdSpec(Guid tenantId, Guid visitId)
    {
        Query.Where(e => e.TenantId == tenantId && e.VisitId == visitId)
            .AsNoTracking();
    }
}
