using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Application.Hospitalizations.Wards.Access;
using Backend.Veteriner.Application.Hospitalizations.Wards.Specs;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.SetWardActive;

public sealed class SetWardActiveCommandHandler : IRequestHandler<SetWardActiveCommand, Result<HospitalizationWardDto>>
{
    private readonly IHospitalizationWardClinicAccess _access;
    private readonly IReadRepository<HospitalizationWard> _wardsRead;
    private readonly IRepository<HospitalizationWard> _wardsWrite;
    private readonly IReadRepository<HospitalizationRoom> _roomsRead;
    private readonly TimeProvider _timeProvider;

    public SetWardActiveCommandHandler(
        IHospitalizationWardClinicAccess access,
        IReadRepository<HospitalizationWard> wardsRead,
        IRepository<HospitalizationWard> wardsWrite,
        IReadRepository<HospitalizationRoom> roomsRead,
        TimeProvider timeProvider)
    {
        _access = access;
        _wardsRead = wardsRead;
        _wardsWrite = wardsWrite;
        _roomsRead = roomsRead;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<HospitalizationWardDto>> Handle(SetWardActiveCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<HospitalizationWardDto>.Failure(access.Error);
        var tenantId = access.Value;

        var ward = await _wardsWrite.FirstOrDefaultAsync(
            new HospitalizationWardByIdSpec(tenantId, request.ClinicId, request.WardId), ct);
        if (ward is null)
            return Result<HospitalizationWardDto>.Failure("HospitalizationWards.NotFound", "Bölüm bulunamadı.");

        if (request.IsActive && !ward.IsActive)
        {
            var duplicate = await _wardsRead.AnyAsync(
                new ActiveHospitalizationWardByClinicAndNameSpec(
                    tenantId, request.ClinicId, HospitalizationWardMapping.NormalizeNameForLookup(ward.Name), ward.Id),
                ct);
            if (duplicate)
            {
                return Result<HospitalizationWardDto>.Failure(
                    "HospitalizationWards.DuplicateName",
                    "Aynı adda aktif bir bölüm var; etkinleştirmeden önce adı değiştirin.");
            }
        }

        if (ward.SetActive(request.IsActive, _timeProvider.GetUtcNow().UtcDateTime))
        {
            await _wardsWrite.UpdateAsync(ward, ct);
            await _wardsWrite.SaveChangesAsync(ct);
        }

        var rooms = await _roomsRead.ListAsync(new HospitalizationRoomsByClinicSpec(tenantId, request.ClinicId), ct);
        return Result<HospitalizationWardDto>.Success(
            HospitalizationWardMapping.ToDto(ward, rooms.Where(r => r.WardId == ward.Id)));
    }
}
