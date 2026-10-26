using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.SetRoomActive;

/// <summary>
/// Odayı pasife alır / yeniden etkinleştirir (idempotent). Pasif oda yeni yatışa atanamaz; odadaki mevcut yatışlar
/// değişmez. Yeniden etkinleştirmede ad benzersizliği tekrar denetlenir.
/// </summary>
public sealed record SetRoomActiveCommand(Guid ClinicId, Guid WardId, Guid RoomId, bool IsActive)
    : IRequest<Result<HospitalizationRoomDto>>;
