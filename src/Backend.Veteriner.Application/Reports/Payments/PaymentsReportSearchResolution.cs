using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Reports.Payments;

internal static class PaymentsReportSearchResolution
{
    /// <summary>
    /// Report (12D-8) ve export (12D-9): <see cref="PaymentsSearchLookupEnabled"/> ile Query DB veya Command DB.
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
