using Ardalis.Specification;
using Backend.Veteriner.Domain.Treatments;

namespace Backend.Veteriner.Application.Examinations.Specs;

public sealed class TreatmentsForExaminationRelatedSpec : Specification<Treatment>
{
    public TreatmentsForExaminationRelatedSpec(Guid tenantId, Guid? clinicId, Guid examinationId, int take)
    {
        Query.Where(t => t.TenantId == tenantId && t.ExaminationId == examinationId);
        if (clinicId.HasValue)
            Query.Where(t => t.ClinicId == clinicId.Value);
        Query.OrderByDescending(t => t.TreatmentDateUtc)
            .ThenByDescending(t => t.Id)
            .Take(take);
    }
}
