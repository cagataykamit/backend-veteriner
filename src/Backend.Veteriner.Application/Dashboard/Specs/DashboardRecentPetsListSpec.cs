using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Dashboard.Specs;

/// <summary><see cref="Pet"/> için <see cref="DashboardRecentClientsListSpec"/> ile aynı sınırlama.</summary>
public sealed record DashboardRecentPetRow(Guid Id, Guid ClientId, string Name, string SpeciesName);

public sealed class DashboardRecentPetsListSpec : Specification<Pet, DashboardRecentPetRow>
{
    public DashboardRecentPetsListSpec(Guid tenantId, int take)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.Id)
            .Take(take)
            .Select(p => new DashboardRecentPetRow(
                p.Id,
                p.ClientId,
                p.Name,
                p.Species != null ? p.Species.Name : string.Empty));
    }
}
