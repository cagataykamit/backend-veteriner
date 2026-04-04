using Ardalis.Specification;
using Backend.Veteriner.Domain.Prescriptions;

namespace Backend.Veteriner.Application.Examinations.Specs;

public sealed class PrescriptionsForExaminationRelatedSpec : Specification<Prescription>
{
    public PrescriptionsForExaminationRelatedSpec(Guid tenantId, Guid? clinicId, Guid examinationId, int take)
    {
        Query.Where(p => p.TenantId == tenantId && p.ExaminationId == examinationId);
        if (clinicId.HasValue)
            Query.Where(p => p.ClinicId == clinicId.Value);
        Query.OrderByDescending(p => p.PrescribedAtUtc)
            .ThenByDescending(p => p.Id)
            .Take(take);
    }
}
