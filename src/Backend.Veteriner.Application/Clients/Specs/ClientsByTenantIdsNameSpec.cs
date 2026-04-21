using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Clients.Specs;

public sealed record ClientNameRow(Guid Id, string FullName);

public sealed class ClientsByTenantIdsNameSpec : Specification<Client, ClientNameRow>
{
    public ClientsByTenantIdsNameSpec(Guid tenantId, IReadOnlyCollection<Guid> clientIds)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && clientIds.Contains(c.Id))
            .Select(c => new ClientNameRow(c.Id, c.FullName ?? string.Empty));
    }
}
