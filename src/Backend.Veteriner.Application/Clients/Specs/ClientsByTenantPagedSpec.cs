using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Clients.Specs;

public sealed class ClientsByTenantPagedSpec : Specification<Client>
{
    public ClientsByTenantPagedSpec(Guid tenantId, int page, int pageSize, string? searchContainsLikePattern)
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

        Query.OrderBy(c => c.FullName)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
