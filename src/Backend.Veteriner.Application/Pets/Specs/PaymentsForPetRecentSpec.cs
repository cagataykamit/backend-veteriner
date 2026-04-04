using Ardalis.Specification;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PaymentsForPetRecentSpec : Specification<Payment>
{
    public PaymentsForPetRecentSpec(Guid tenantId, Guid? clinicId, Guid petId, int take)
    {
        Query.Where(p => p.TenantId == tenantId && p.PetId == petId);
        if (clinicId.HasValue)
            Query.Where(p => p.ClinicId == clinicId.Value);
        Query.OrderByDescending(p => p.PaidAtUtc)
            .ThenByDescending(p => p.Id)
            .Take(take);
    }
}
