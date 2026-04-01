using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Clients.Specs;

public sealed class ClientsByTenantCountSpec : Specification<Client>
{
    public ClientsByTenantCountSpec(Guid tenantId, string? searchContainsLikePattern)
    {
        Query.Where(c => c.TenantId == tenantId);
        if (searchContainsLikePattern is not null)
        {
            var p = searchContainsLikePattern;
            Query.Where(c =>
                EF.Functions.Like(c.FullName, p)
                || (c.Email != null && EF.Functions.Like(c.Email, p))
                || (c.Phone != null && EF.Functions.Like(c.Phone, p))
                || (c.PhoneNormalized != null && EF.Functions.Like(c.PhoneNormalized, p)));
        }
    }
}
