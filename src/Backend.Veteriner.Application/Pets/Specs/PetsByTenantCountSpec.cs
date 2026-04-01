using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PetsByTenantCountSpec : Specification<Pet>
{
    public PetsByTenantCountSpec(
        Guid tenantId,
        Guid? clientId,
        Guid? speciesId,
        string? searchContainsLikePattern,
        Guid[] petIdsMatchingClientTextOrEmpty)
    {
        Query.Where(p => p.TenantId == tenantId);
        if (clientId.HasValue)
            Query.Where(p => p.ClientId == clientId.Value);
        if (speciesId.HasValue)
            Query.Where(p => p.SpeciesId == speciesId.Value);
        if (searchContainsLikePattern is not null)
        {
            var pat = searchContainsLikePattern;
            var ownerPets = petIdsMatchingClientTextOrEmpty;
            Query.Where(p =>
                EF.Functions.Like(p.Name, pat)
                || (p.Breed != null && EF.Functions.Like(p.Breed, pat))
                || EF.Functions.Like(p.Species!.Name, pat)
                || (p.BreedRef != null && EF.Functions.Like(p.BreedRef.Name, pat))
                || (ownerPets.Length > 0 && ownerPets.Contains(p.Id)));
        }
    }
}
