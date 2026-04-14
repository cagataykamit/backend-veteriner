using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Dashboard.Specs;

/// <summary>
/// <see cref="Client"/> üzerinde oluşturma zamanı yok; <c>Id</c> azalan sıra yaklaşık “yeni” örneklemesi sağlar (GUID v4 kronolojik değildir).
/// </summary>
public sealed record DashboardRecentClientRow(Guid Id, string FullName, string? Phone);

public sealed class DashboardRecentClientsListSpec : Specification<Client, DashboardRecentClientRow>
{
    public DashboardRecentClientsListSpec(Guid tenantId, int take)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.Id)
            .Take(take)
            .Select(c => new DashboardRecentClientRow(c.Id, c.FullName, c.Phone));
    }
}
