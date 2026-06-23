using Ardalis.Specification;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PaymentsForPetRecentSpec : Specification<Payment>
{
    public PaymentsForPetRecentSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        int take,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.Where(p => p.TenantId == tenantId && p.PetId == petId);
        DashboardSpecificationClinicScope.ApplyToPayment(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(p => p.PaidAtUtc)
            .ThenByDescending(p => p.Id)
            .Take(take);
    }
}
