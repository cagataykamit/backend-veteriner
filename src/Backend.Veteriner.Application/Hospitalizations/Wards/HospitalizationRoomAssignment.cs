using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Wards.Specs;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Hospitalizations.Wards;

/// <summary>Yatışa atanacak oda ve bölümü (konum metni için) birlikte taşır.</summary>
public sealed record HospitalizationRoomAssignment(HospitalizationRoom Room, HospitalizationWard Ward);

/// <summary>
/// Yatış oluştur/güncelle komutlarında oda ataması kuralı tek yerde: oda kiracıya ve yatışın kliniğine ait olmalı
/// (değilse <c>Hospitalizations.RoomNotFound</c>, başka kiracı/klinik sızdırılmaz), oda ve bölümü aktif olmalı
/// (<c>Hospitalizations.RoomInactive</c>). Mevcut yatışın zaten bağlı olduğu oda pasifleşmişse yatış etkilenmez:
/// aynı oda yeniden gönderildiğinde aktiflik denetlenmez.
/// </summary>
public static class HospitalizationRoomAssignmentResolver
{
    public static async Task<Result<HospitalizationRoomAssignment>> ResolveAsync(
        IReadRepository<HospitalizationRoom> rooms,
        IReadRepository<HospitalizationWard> wards,
        Guid tenantId,
        Guid clinicId,
        Guid roomId,
        Guid? currentRoomId,
        CancellationToken ct)
    {
        var room = await rooms.FirstOrDefaultAsync(new HospitalizationRoomByIdSpec(tenantId, clinicId, roomId), ct);
        if (room is null)
        {
            return Result<HospitalizationRoomAssignment>.Failure(
                "Hospitalizations.RoomNotFound",
                "Oda bulunamadı veya bu kliniğe ait değil.");
        }

        var ward = await wards.FirstOrDefaultAsync(new HospitalizationWardByIdSpec(tenantId, clinicId, room.WardId), ct);
        if (ward is null)
        {
            return Result<HospitalizationRoomAssignment>.Failure(
                "Hospitalizations.RoomNotFound",
                "Odanın bölümü bulunamadı.");
        }

        var keepsCurrentRoom = currentRoomId.HasValue && currentRoomId.Value == room.Id;
        if (!keepsCurrentRoom)
        {
            if (!room.IsActive)
            {
                return Result<HospitalizationRoomAssignment>.Failure(
                    "Hospitalizations.RoomInactive",
                    "Pasif oda yeni yatışa atanamaz.");
            }

            if (!ward.IsActive)
            {
                return Result<HospitalizationRoomAssignment>.Failure(
                    "Hospitalizations.RoomInactive",
                    "Pasif bölümdeki oda yeni yatışa atanamaz.");
            }
        }

        return Result<HospitalizationRoomAssignment>.Success(new HospitalizationRoomAssignment(room, ward));
    }
}
