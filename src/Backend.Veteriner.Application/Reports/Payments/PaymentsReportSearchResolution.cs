using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Reports.Payments;

internal static class PaymentsReportSearchResolution
{
    /// <summary>
    /// Export pipeline (12D-9) için mevcut imza — Command DB resolution; değişmedi.
    /// </summary>
    public static async Task<(string? Pattern, Guid[] ClientIds, Guid[] PetIds)> ResolveSearchAsync(
        Guid tenantId,
        string? search,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        CancellationToken ct)
    {
        var normalizedSearch = ListQueryTextSearch.Normalize(search);
        if (normalizedSearch is null)
            return (null, [], []);

        var searchPattern = ListQueryTextSearch.BuildContainsLikePattern(normalizedSearch);
        var nameClients = await clients.ListAsync(
            new ClientsByTenantTextSearchSpec(tenantId, searchPattern), ct);
        var searchClientIds = nameClients.Select(c => c.Id).Distinct().ToArray();
        var petsMatchingText = await pets.ListAsync(
            new PetsByTenantTextFieldsSearchSpec(tenantId, searchPattern), ct);
        var searchPetIds = petsMatchingText.Select(p => p.Id).Distinct().ToArray();
        return (searchPattern, searchClientIds, searchPetIds);
    }

    /// <summary>
    /// Report handler (12D-8): <see cref="PaymentsSearchLookupEnabled"/> ile Query DB veya Command DB.
    /// </summary>
    public static async Task<(string? Pattern, Guid[] ClientIds, Guid[] PetIds)> ResolveSearchAsync(
        Guid tenantId,
        string? search,
        bool paymentsSearchLookupEnabled,
        IClientReadModelLookupReader clientLookupReader,
        IPetReadModelLookupReader petLookupReader,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        CancellationToken ct)
    {
        var normalizedSearch = ListQueryTextSearch.Normalize(search);
        if (normalizedSearch is null)
            return (null, [], []);

        var searchPattern = ListQueryTextSearch.BuildContainsLikePattern(normalizedSearch);
        var (searchClientIds, searchPetIds) = await PaymentsListSearchResolution.ResolveSearchIdsAsync(
            tenantId,
            searchPattern,
            paymentsSearchLookupEnabled,
            clientLookupReader,
            petLookupReader,
            clients,
            pets,
            ct);
        return (searchPattern, searchClientIds, searchPetIds);
    }
}
