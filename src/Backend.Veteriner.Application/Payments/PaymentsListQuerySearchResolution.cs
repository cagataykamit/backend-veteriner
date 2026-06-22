using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Pets.ReadModels;

namespace Backend.Veteriner.Application.Payments;

/// <summary>
/// CQRS-15L: Payment list Query DB search path için client/pet ID lookup (yalnız Query DB reader'lar).
/// Command DB spec'leri veya <see cref="PaymentsListSearchResolution"/> kullanılmaz.
/// </summary>
public static class PaymentsListQuerySearchResolution
{
    public static async Task<(Guid[] ClientIds, Guid[] PetIds)> ResolveSearchIdsAsync(
        Guid tenantId,
        string searchPattern,
        IClientReadModelLookupReader clientLookupReader,
        IPetReadModelLookupReader petLookupReader,
        CancellationToken ct)
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
}
