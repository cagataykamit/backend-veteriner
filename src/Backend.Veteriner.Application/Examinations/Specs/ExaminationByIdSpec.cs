using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Examinations.Specs;

public sealed class ExaminationByIdSpec : Specification<Examination>
{
    public ExaminationByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(e => e.TenantId == tenantId && e.Id == id);
    }
}
