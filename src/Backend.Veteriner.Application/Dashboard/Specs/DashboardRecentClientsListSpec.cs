using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;

namespace Backend.Veteriner.Application.Dashboard.Specs;

/// <summary>
/// <see cref="Client"/> üzerinde oluşturma zamanı yok; <c>Id</c> azalan sıra yaklaşık “yeni” örneklemesi sağlar (GUID v4 kronolojik değildir).
/// </summary>
public sealed class DashboardRecentClientsListSpec : Specification<Client>
{
    public DashboardRecentClientsListSpec(Guid tenantId, int take)
    {
        Query.Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.Id)
            .Take(take);
    }
}
