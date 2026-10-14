using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

/// <summary>
/// Aynı müşteri için aynı isim (Türkçe duyarlı, aksan duyarsız anahtar: <see cref="Pet.NormalizeNameForDuplicateCheck"/>)
/// + aynı tür (SpeciesId) ile ikinci kaydı engeller.
/// </summary>
public sealed class PetByClientNameAndSpeciesIdSpec : Specification<Pet>
{
    public PetByClientNameAndSpeciesIdSpec(Guid clientId, string nameDuplicateKey, Guid speciesId)
    {
        Query.Where(p => p.ClientId == clientId && p.SpeciesId == speciesId);
        Query.Where(PetNameDuplicateKey.Matches(nameDuplicateKey));
    }
}
