using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class PetByClientNameAndSpeciesIdExcludingIdSpec : Specification<Pet>
{
    public PetByClientNameAndSpeciesIdExcludingIdSpec(Guid clientId, string nameLowerInvariant, Guid speciesId, Guid excludeId)
    {
        Query.Where(p =>
            p.ClientId == clientId
            && p.Name.ToLower() == nameLowerInvariant
            && p.SpeciesId == speciesId
            && p.Id != excludeId);
    }
}