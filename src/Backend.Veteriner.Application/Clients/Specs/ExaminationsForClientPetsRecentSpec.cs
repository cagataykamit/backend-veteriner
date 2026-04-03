using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;

namespace Backend.Veteriner.Application.Clients.Specs;

public sealed class ExaminationsForClientPetsRecentSpec : Specification<Examination>
{
    public ExaminationsForClientPetsRecentSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid[] petIds,
        int take)
    {
        Query.Where(e => e.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(e => e.ClinicId == clinicId.Value);
        Query.Where(e => petIds.Contains(e.PetId));
        Query.OrderByDescending(e => e.ExaminedAtUtc)
            .ThenByDescending(e => e.Id)
            .Take(take);
    }
}
