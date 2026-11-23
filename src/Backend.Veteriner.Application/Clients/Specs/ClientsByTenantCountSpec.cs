using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Specs;

public sealed class ClientsByTenantCountSpec : Specification<Client>
{
    /// <param name="includeDeleted">Yalnız "kiracıda veri var mı" denetimi (örnek klinik yükleme) için; listeler silinenleri saymaz (ADR-054).</param>
    public ClientsByTenantCountSpec(Guid tenantId, DailySearchTerm? searchTerm, bool includeDeleted = false)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId);
        if (!includeDeleted)
            Query.Where(Client.NotDeleted);
        if (searchTerm is not null)
            Query.WhereDailySearch(searchTerm);
    }
}
