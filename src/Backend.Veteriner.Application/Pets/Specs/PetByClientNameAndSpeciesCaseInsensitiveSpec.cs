using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

/// <summary>
/// Aynı müşteri için aynı isim + tür (case-insensitive) ile ikinci kaydı engeller;
/// farklı ırk veya doğum tarihi aynı kayıt olarak görülmez.
/// </summary>
public sealed class PetByClientNameAndSpeciesCaseInsensitiveSpec : Specification<Pet>
{
    public PetByClientNameAndSpeciesCaseInsensitiveSpec(Guid clientId, string nameLowerInvariant, string speciesLowerInvariant)
    {
        Query.Where(p =>
            p.ClientId == clientId
            && p.Name.ToLower() == nameLowerInvariant
            && p.Species.ToLower() == speciesLowerInvariant);
    }
}
