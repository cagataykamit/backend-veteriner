using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PetsByTenantPagedSpec : Specification<Pet, PetListProjectionRow>
{
    public PetsByTenantPagedSpec(
        Guid tenantId,
        int page,
        int pageSize,
        Guid? clientId,
        Guid? speciesId,
        string? searchContainsLikePattern,
        Guid[] petIdsMatchingClientTextOrEmpty)
    {
        Query.AsNoTracking();
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
