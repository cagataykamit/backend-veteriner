using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Examinations.Specs;

/// <summary>Verilen gelişlere bağlı muayeneler, klinik kapsamıyla; en yeni önce.</summary>
public sealed class ExaminationsByVisitIdsSpec : Specification<Examination>
{
    public ExaminationsByVisitIdsSpec(
        Guid tenantId,
        IReadOnlyCollection<Guid> visitIds,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.Where(e => e.TenantId == tenantId && e.VisitId != null && visitIds.Contains(e.VisitId.Value));
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderByDescending(e => e.ExaminedAtUtc)
            .ThenByDescending(e => e.Id)
            .AsNoTracking();
    }
}
