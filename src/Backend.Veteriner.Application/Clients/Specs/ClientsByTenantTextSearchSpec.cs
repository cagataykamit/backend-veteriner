using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Clients.Specs;

/// <summary>Kiracı içinde müşteri metin araması (ad, e-posta, telefon).</summary>
public sealed class ClientsByTenantTextSearchSpec : Specification<Client>
{
    public ClientsByTenantTextSearchSpec(Guid tenantId, string containsLikePattern)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId)
            .Where(c =>
                EF.Functions.Like(c.FullName, containsLikePattern)
                || (c.Email != null && EF.Functions.Like(c.Email, containsLikePattern))
                || (c.Phone != null && EF.Functions.Like(c.Phone, containsLikePattern))
                || (c.PhoneNormalized != null
                    && EF.Functions.Like(c.PhoneNormalized, containsLikePattern)));
    }
}
