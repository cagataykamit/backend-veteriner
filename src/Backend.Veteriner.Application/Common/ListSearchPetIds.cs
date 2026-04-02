using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Common;

/// <summary>Randevu/muayene/aşı listelerinde müşteri veya hayvan metin alanları araması için eşleşen pet id kümesi.</summary>
public static class ListSearchPetIds
{
    /// <summary>Hayvan kartı metin eşleşmesi (ad/tür/ırk; hayvan listesi ile aynı) + müşteri metin eşleşmesine göre sahip olunan petlerin birleşimi.</summary>
    public static async Task<Guid[]> ResolveForAggregateListAsync(
        Guid tenantId,
        string containsLikePattern,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        CancellationToken ct)
    {
        var petTextMatched = await pets.ListAsync(new PetsByTenantTextFieldsSearchSpec(tenantId, containsLikePattern), ct);
        var ids = new HashSet<Guid>(petTextMatched.Select(p => p.Id));

        var matchedClients = await clients.ListAsync(new ClientsByTenantTextSearchSpec(tenantId, containsLikePattern), ct);
        var clientIds = matchedClients.Select(c => c.Id).Distinct().ToArray();
        if (clientIds.Length > 0)
        {
            var owned = await pets.ListAsync(new PetsByTenantForClientIdsSpec(tenantId, clientIds), ct);
            foreach (var p in owned)
                ids.Add(p.Id);
        }

        return ids.Count > 0 ? ids.ToArray() : [];
    }
}
