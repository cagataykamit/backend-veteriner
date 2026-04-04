using Ardalis.Specification;
using Backend.Veteriner.Domain.LabResults;

namespace Backend.Veteriner.Application.Examinations.Specs;

public sealed class LabResultsForExaminationRelatedSpec : Specification<LabResult>
{
    public LabResultsForExaminationRelatedSpec(Guid tenantId, Guid? clinicId, Guid examinationId, int take)
    {
        Query.Where(l => l.TenantId == tenantId && l.ExaminationId == examinationId);
        if (clinicId.HasValue)
            Query.Where(l => l.ClinicId == clinicId.Value);
        Query.OrderByDescending(l => l.ResultDateUtc)
            .ThenByDescending(l => l.Id)
            .Take(take);
    }
}
