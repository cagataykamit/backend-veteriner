using System.Linq.Expressions;
using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

/// <summary>
/// Kiracıda bakım iş akışına girmeyen (vefat) hayvanların kimlikleri; gecikmiş/yaklaşan aşı sayım ve listelerinde dışlama için.
/// Koşul <see cref="Pet.ReceivesCare"/> ile tek yerde (burada yalnız tersi alınır).
/// </summary>
public sealed class PetIdsNotReceivingCareSpec : Specification<Pet, Guid>
{
    private static readonly Expression<Func<Pet, bool>> NotReceivingCare =
        Expression.Lambda<Func<Pet, bool>>(Expression.Not(Pet.ReceivesCare.Body), Pet.ReceivesCare.Parameters);

    public PetIdsNotReceivingCareSpec(Guid tenantId)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId);
        Query.Where(NotReceivingCare);
        Query.Select(p => p.Id);
    }
}
