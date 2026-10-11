using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Examinations.Specs;

public sealed class ExaminationAddendaByExaminationIdSpec : Specification<ExaminationAddendum>
{
    public ExaminationAddendaByExaminationIdSpec(Guid tenantId, Guid examinationId)
    {
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId && a.ExaminationId == examinationId);
        Query.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id);
    }
}
