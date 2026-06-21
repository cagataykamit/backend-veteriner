using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Payments;

/// <summary>
/// Strateji B payment list search resolution (CQRS-12D-7): ayrı client id + pet id kümeleri.
/// Flag kapalıyken Command DB spec'leri; açıkken Query DB lookup reader'ları.
/// </summary>
public static class PaymentsListSearchResolution
{
    public static async Task<(Guid[] ClientIds, Guid[] PetIds)> ResolveSearchIdsAsync(
        Guid tenantId,
        string searchPattern,
        bool paymentsSearchLookupEnabled,
        IClientReadModelLookupReader clientLookupReader,
        IPetReadModelLookupReader petLookupReader,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        CancellationToken ct)
    {
        if (paymentsSearchLookupEnabled)
        {
            var clientLookup = await clientLookupReader.ResolveClientIdsByTextSearchAsync(
                new ClientTextSearchLookupRequest(tenantId, searchPattern),
                ct);
            var petLookup = await petLookupReader.ResolvePetIdsByPetTextFieldsAsync(
                new PetTextFieldsSearchLookupRequest(tenantId, searchPattern),
                ct);

            return (
                clientLookup.ClientIds.Count > 0 ? clientLookup.ClientIds.ToArray() : [],
                petLookup.PetIds.Count > 0 ? petLookup.PetIds.ToArray() : []);
        }

        var nameClients = await clients.ListAsync(
            new ClientsByTenantTextSearchSpec(tenantId, searchPattern),
            ct);
        var searchClientIds = nameClients.Select(c => c.Id).Distinct().ToArray();

        var petsMatchingText = await pets.ListAsync(
            new PetsByTenantTextFieldsSearchSpec(tenantId, searchPattern),
            ct);
        var searchPetIds = petsMatchingText.Select(p => p.Id).Distinct().ToArray();

        return (searchClientIds, searchPetIds);
    }
}
