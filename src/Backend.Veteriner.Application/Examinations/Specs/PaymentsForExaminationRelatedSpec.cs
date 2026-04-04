using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Examinations.Specs;

public sealed class PaymentsForExaminationRelatedSpec : Specification<Payment>
{
    public PaymentsForExaminationRelatedSpec(Guid tenantId, Guid? clinicId, Guid examinationId, int take)
    {
        Query.Where(p => p.TenantId == tenantId && p.ExaminationId == examinationId);
        if (clinicId.HasValue)
            Query.Where(p => p.ClinicId == clinicId.Value);
        Query.OrderByDescending(p => p.PaidAtUtc)
            .ThenByDescending(p => p.Id)
            .Take(take);
    }
}
