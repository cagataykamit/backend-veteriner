using Ardalis.Specification;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Examinations.Specs;

public sealed class HospitalizationsForExaminationRelatedSpec : Specification<Hospitalization>
{
    public HospitalizationsForExaminationRelatedSpec(Guid tenantId, Guid? clinicId, Guid examinationId, int take)
    {
        Query.Where(h => h.TenantId == tenantId && h.ExaminationId == examinationId);
        if (clinicId.HasValue)
            Query.Where(h => h.ClinicId == clinicId.Value);
        Query.OrderByDescending(h => h.AdmittedAtUtc)
            .ThenByDescending(h => h.Id)
            .Take(take);
    }
}
