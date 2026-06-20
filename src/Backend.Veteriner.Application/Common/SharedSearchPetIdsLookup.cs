using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Common;

/// <summary>
/// Strateji A klinik aggregate listeleri için paylaşılan search pet-id çözümlemesi (CQRS-12D-3+).
/// Flag kapalıyken Command DB <see cref="ListSearchPetIds"/>; açıkken Query DB lookup reader.
/// </summary>
public static class SharedSearchPetIdsLookup
{
    public static async Task<Guid[]> ResolveAsync(
        Guid tenantId,
        string searchPattern,
        bool sharedSearchLookupEnabled,
        IPetReadModelLookupReader petLookupReader,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        CancellationToken ct)
    {
        if (sharedSearchLookupEnabled)
        {
            var lookup = await petLookupReader.ResolvePetIdsByTextSearchAsync(
                new PetTextSearchLookupRequest(tenantId, searchPattern),
                ct);
            return lookup.PetIds.Count > 0 ? lookup.PetIds.ToArray() : [];
        }

        return await ListSearchPetIds.ResolveForAggregateListAsync(
            tenantId,
            searchPattern,
            clients,
            pets,
            ct);
    }
}
