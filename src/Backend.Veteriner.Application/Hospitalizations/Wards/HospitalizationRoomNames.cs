using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Wards.Specs;
using Backend.Veteriner.Domain.Hospitalizations;

namespace Backend.Veteriner.Application.Hospitalizations.Wards;

/// <summary>Yatış DTO'larında gösterilen oda ve bölüm adı.</summary>
public sealed record HospitalizationRoomNames(string RoomName, string WardName);

/// <summary>
/// Yatış okuma DTO'ları için oda → (oda adı, bölüm adı) çözümü tek yerde; toplu okur (liste için N+1 yok).
/// Pasif oda/bölüm adları da döner: mevcut yatışın konumu görünür kalmalıdır.
/// </summary>
public static class HospitalizationRoomNamesLookup
{
    public static async Task<IReadOnlyDictionary<Guid, HospitalizationRoomNames>> ResolveAsync(
        IReadRepository<HospitalizationRoom> rooms,
        IReadRepository<HospitalizationWard> wards,
        Guid tenantId,
        IEnumerable<Guid?> roomIds,
        CancellationToken ct)
    {
        var ids = roomIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, HospitalizationRoomNames>();

        var roomRows = await rooms.ListAsync(new HospitalizationRoomsByIdsSpec(tenantId, ids), ct);
        var wardIds = roomRows.Select(r => r.WardId).Distinct().ToArray();
        var wardRows = wardIds.Length == 0
            ? []
            : await wards.ListAsync(new HospitalizationWardsByIdsSpec(tenantId, wardIds), ct);
        var wardNameById = wardRows.ToDictionary(w => w.Id, w => w.Name);

        return roomRows.ToDictionary(
            r => r.Id,
            r => new HospitalizationRoomNames(
                r.Name,
                wardNameById.TryGetValue(r.WardId, out var wardName) ? wardName : string.Empty));
    }
}
