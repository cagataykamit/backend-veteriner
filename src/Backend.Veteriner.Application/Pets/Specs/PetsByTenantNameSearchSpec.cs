using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Pets.Specs;

/// <summary>Kiracı içinde hayvan adına göre arama (ödeme listesi araması için id çözümü).</summary>
public sealed class PetsByTenantNameSearchSpec : Specification<Pet>
{
    public PetsByTenantNameSearchSpec(Guid tenantId, string containsLikePattern)
    {
        Query.Where(p => p.TenantId == tenantId)
            .Where(p => EF.Functions.Like(p.Name, containsLikePattern));
    }
}
