using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Reports.Appointments;

internal static class AppointmentsReportSearchHelper
{
    public static async Task<(string? Pattern, Guid[] PetIds)> ResolveAsync(
        Guid tenantId,
        string? search,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        CancellationToken ct)
    {
        var normalizedSearch = ListQueryTextSearch.Normalize(search);
        if (normalizedSearch is null)
            return (null, []);

        var searchPattern = ListQueryTextSearch.BuildContainsLikePattern(normalizedSearch);
        var searchPetIds = await ListSearchPetIds.ResolveForAggregateListAsync(
            tenantId,
            searchPattern,
            clients,
            pets,
            ct);
        return (searchPattern, searchPetIds);
    }
}
