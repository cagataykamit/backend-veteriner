using Ardalis.Specification;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Domain.Examinations;

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
        DashboardSpecificationClinicScope.ApplyToExamination(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(e => e.ExaminedAtUtc)
            .ThenByDescending(e => e.Id)
            .AsNoTracking();
    }
}
