using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Wards.Access;
using Backend.Veteriner.Application.Hospitalizations.Wards.Specs;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.CreateRoom;

public sealed class CreateRoomCommandHandler : IRequestHandler<CreateRoomCommand, Result<Guid>>
{
    private readonly IHospitalizationWardClinicAccess _access;
    private readonly IReadRepository<HospitalizationWard> _wardsRead;
    private readonly IReadRepository<HospitalizationRoom> _roomsRead;
    private readonly IRepository<HospitalizationRoom> _roomsWrite;
    private readonly TimeProvider _timeProvider;

    public CreateRoomCommandHandler(
        IHospitalizationWardClinicAccess access,
        IReadRepository<HospitalizationWard> wardsRead,
        IReadRepository<HospitalizationRoom> roomsRead,
        IRepository<HospitalizationRoom> roomsWrite,
        TimeProvider timeProvider)
    {
        _access = access;
        _wardsRead = wardsRead;
        _roomsRead = roomsRead;
        _roomsWrite = roomsWrite;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid>> Handle(CreateRoomCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<Guid>.Failure(access.Error);
        var tenantId = access.Value;

        var ward = await _wardsRead.FirstOrDefaultAsync(
            new HospitalizationWardByIdSpec(tenantId, request.ClinicId, request.WardId), ct);
        if (ward is null)
            return Result<Guid>.Failure("HospitalizationWards.NotFound", "Bölüm bulunamadı.");

        var duplicate = await _roomsRead.AnyAsync(
            new ActiveHospitalizationRoomByWardAndNameSpec(
                tenantId, ward.Id, HospitalizationWardMapping.NormalizeNameForLookup(request.Name)),
            ct);
        if (duplicate)
        {
            return Result<Guid>.Failure(
                "HospitalizationRooms.DuplicateName",
                "Bu bölümde aynı adda aktif bir oda zaten var.");
        }

        HospitalizationRoom room;
        try
        {
            room = HospitalizationRoom.Create(ward, request.Name, request.SortOrder, _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (ArgumentException ex)
        {
            return Result<Guid>.Failure("HospitalizationRooms.Validation", ex.Message);
        }

        await _roomsWrite.AddAsync(room, ct);
        await _roomsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(room.Id);
    }
}
