using Ardalis.Specification;
using Backend.Veteriner.Domain.Charges;

namespace Backend.Veteriner.Application.Charges.Specs;

/// <summary>İzlenen (tracked) ücretler: tahsis sayacı concurrency token ile güncellenir.</summary>
public sealed class ChargesByIdsForUpdateSpec : Specification<Charge>
{
    public ChargesByIdsForUpdateSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        Query.Where(c => c.TenantId == tenantId && ids.Contains(c.Id));
    }
}
