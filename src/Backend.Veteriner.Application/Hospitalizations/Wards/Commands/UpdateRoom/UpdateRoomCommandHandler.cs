using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Application.Hospitalizations.Wards.Access;
using Backend.Veteriner.Application.Hospitalizations.Wards.Specs;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.UpdateRoom;

public sealed class UpdateRoomCommandHandler : IRequestHandler<UpdateRoomCommand, Result<HospitalizationRoomDto>>
{
    private readonly IHospitalizationWardClinicAccess _access;
    private readonly IReadRepository<HospitalizationRoom> _roomsRead;
    private readonly IRepository<HospitalizationRoom> _roomsWrite;
    private readonly TimeProvider _timeProvider;

    public UpdateRoomCommandHandler(
        IHospitalizationWardClinicAccess access,
        IReadRepository<HospitalizationRoom> roomsRead,
        IRepository<HospitalizationRoom> roomsWrite,
        TimeProvider timeProvider)
    {
        _access = access;
        _roomsRead = roomsRead;
        _roomsWrite = roomsWrite;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<HospitalizationRoomDto>> Handle(UpdateRoomCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<HospitalizationRoomDto>.Failure(access.Error);
        var tenantId = access.Value;

        var room = await _roomsWrite.FirstOrDefaultAsync(
            new HospitalizationRoomByIdSpec(tenantId, request.ClinicId, request.RoomId, request.WardId), ct);
        if (room is null)
            return Result<HospitalizationRoomDto>.Failure("HospitalizationRooms.NotFound", "Oda bulunamadı.");

        if (room.IsActive)
        {
            var duplicate = await _roomsRead.AnyAsync(
                new ActiveHospitalizationRoomByWardAndNameSpec(
                    tenantId, room.WardId, HospitalizationWardMapping.NormalizeNameForLookup(request.Name), room.Id),
                ct);
            if (duplicate)
            {
                return Result<HospitalizationRoomDto>.Failure(
                    "HospitalizationRooms.DuplicateName",
                    "Bu bölümde aynı adda aktif bir oda zaten var.");
            }
        }

        try
        {
            room.Update(request.Name, request.SortOrder, _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (ArgumentException ex)
        {
            return Result<HospitalizationRoomDto>.Failure("HospitalizationRooms.Validation", ex.Message);
        }

        await _roomsWrite.UpdateAsync(room, ct);
        await _roomsWrite.SaveChangesAsync(ct);
        return Result<HospitalizationRoomDto>.Success(HospitalizationWardMapping.ToDto(room));
    }
}
