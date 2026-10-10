using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PetsByTenantCountSpec : Specification<Pet>
{
    public PetsByTenantCountSpec(
        Guid tenantId,
        Guid? clientId,
        Guid? speciesId,
        DailySearchTerm? searchTerm,
        Guid[] petIdsMatchingClientTextOrEmpty)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId);
        if (clientId.HasValue)
            Query.Where(p => p.ClientId == clientId.Value);
        if (speciesId.HasValue)
            Query.Where(p => p.SpeciesId == speciesId.Value);
        if (searchTerm is not null)
            Query.WhereDailySearch(searchTerm, petIdsMatchingClientTextOrEmpty);
    }
}
