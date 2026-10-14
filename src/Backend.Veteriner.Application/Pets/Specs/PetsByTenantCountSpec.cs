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
        PetStatus? status,
        DailySearchTerm? searchTerm,
        Guid[] petIdsMatchingClientTextOrEmpty)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId);
        if (clientId.HasValue)
            Query.Where(p => p.ClientId == clientId.Value);
        if (status.HasValue)
            Query.Where(p => p.Status == status.Value);
        if (speciesId.HasValue)
            Query.Where(p => p.SpeciesId == speciesId.Value);
        if (searchTerm is not null)
            Query.WhereDailySearch(searchTerm, petIdsMatchingClientTextOrEmpty);
    }
}
