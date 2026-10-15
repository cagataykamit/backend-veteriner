using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Common;

/// <summary>
/// Hayvana bağlı klinik listelerinde (randevu/muayene/aşı) <c>clientId</c> süzgeci: müşterinin kiracı içindeki hayvan kimlikleri.
/// Diğer süzgeçlerle AND; müşterinin hayvanı yoksa veya müşteri başka kiracıdaysa boş küme döner (liste boş, hata değil).
/// </summary>
public static class ClientListPetIds
{
    /// <returns><c>clientId</c> yoksa <c>null</c> (süzgeç uygulanmaz); aksi halde müşterinin hayvan kimlikleri (boş olabilir).</returns>
    public static async Task<Guid[]?> ResolveAsync(
        Guid tenantId,
        Guid? clientId,
        IReadRepository<Pet> pets,
        CancellationToken ct)
    {
        if (clientId is not { } id)
            return null;

        var owned = await pets.ListAsync(new PetsByTenantForClientIdsSpec(tenantId, [id]), ct);
        return owned.Select(p => p.Id).ToArray();
    }
}
