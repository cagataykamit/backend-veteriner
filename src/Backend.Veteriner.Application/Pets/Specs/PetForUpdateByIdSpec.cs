using Ardalis.Specification;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.Specs;

/// <summary>
/// İzlenmeyen hayvan kaydı; istemcinin gönderdiği RowVersion beklenen sürüm olarak eklenip güncellenir
/// (izlenen kayıtta özgün sürüm veritabanından okunanda kalırdı).
/// </summary>
public sealed class PetForUpdateByIdSpec : Specification<Pet>
{
    public PetForUpdateByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && p.Id == id);
    }
}
