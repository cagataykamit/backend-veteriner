using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Reports.Payments;

internal static class PaymentsReportSearchResolution
{
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
}
