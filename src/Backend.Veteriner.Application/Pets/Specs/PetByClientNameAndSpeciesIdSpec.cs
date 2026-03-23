using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

/// <summary>
/// Aynı müşteri için aynı isim + aynı tür (SpeciesId) ile ikinci kaydı engeller.
/// </summary>
public sealed class PetByClientNameAndSpeciesIdSpec : Specification<Pet>
{
    public PetByClientNameAndSpeciesIdSpec(Guid clientId, string nameLowerInvariant, Guid speciesId)
    {
        Query.Where(p =>
            p.ClientId == clientId
            && p.Name.ToLower() == nameLowerInvariant
            && p.SpeciesId == speciesId);
    }
}
