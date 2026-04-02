using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Pets.Specs;

/// <summary>
/// Kiracı içinde hayvan kartında metin araması: ad, serbest ırk metni, tür adı, katalog ırk adı.
/// <see cref="PetsByTenantPagedSpec"/> / <see cref="PetsByTenantCountSpec"/> ile aynı LIKE kümesi; ödeme ve randevu/muayene/aşı listelerinde pet id çözümü için kullanılır.
/// </summary>
public sealed class PetsByTenantTextFieldsSearchSpec : Specification<Pet>
{
    public PetsByTenantTextFieldsSearchSpec(Guid tenantId, string containsLikePattern)
    {
        Query.Where(p => p.TenantId == tenantId)
            .Where(p =>
                EF.Functions.Like(p.Name, containsLikePattern)
                || (p.Breed != null && EF.Functions.Like(p.Breed, containsLikePattern))
                || EF.Functions.Like(p.Species!.Name, containsLikePattern)
                || (p.BreedRef != null && EF.Functions.Like(p.BreedRef.Name, containsLikePattern)));
    }
}
