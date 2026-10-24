using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Hospitalizations.Wards;

/// <summary>Bölüm/oda DTO dönüşümü ve ad karşılaştırma anahtarı tek yerde (liste ve yazma yanıtları aynı biçimi döner).</summary>
internal static class HospitalizationWardMapping
{
    public static HospitalizationWardDto ToDto(HospitalizationWard ward, IEnumerable<HospitalizationRoom> rooms)
        => new(
            ward.Id,
            ward.ClinicId,
            ward.Name,
            ward.SortOrder,
            ward.IsActive,
            rooms
                .OrderBy(r => r.SortOrder)
                .ThenBy(r => r.Name)
                .Select(ToDto)
                .ToList());

    public static HospitalizationRoomDto ToDto(HospitalizationRoom room)
        => new(room.Id, room.WardId, room.Name, room.SortOrder, room.IsActive);

    /// <summary>Benzersizlik denetiminde kullanılan anahtar (case-insensitive spec'lerle aynı normalizasyon).</summary>
    public static string NormalizeNameForLookup(string name)
        => name.Trim().ToLowerInvariant();
}
