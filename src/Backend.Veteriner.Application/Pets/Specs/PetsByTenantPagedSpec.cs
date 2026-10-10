using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PetsByTenantPagedSpec : Specification<Pet, PetListProjectionRow>
{
    public PetsByTenantPagedSpec(
        Guid tenantId,
        int page,
        int pageSize,
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

        Query.OrderBy(p => p.Name)
            .ThenBy(p => p.Species!.Name)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        Query.Select(p => new PetListProjectionRow(
            p.Id,
            p.TenantId,
            p.ClientId,
            p.Name,
            p.SpeciesId,
            p.Species!.Name,
            p.ColorId,
            p.ColorRef != null ? p.ColorRef.Name : null,
            p.Breed,
            p.Weight));
    }
}
