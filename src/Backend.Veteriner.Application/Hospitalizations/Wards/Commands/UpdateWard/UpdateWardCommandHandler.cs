using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Application.Hospitalizations.Wards.Access;
using Backend.Veteriner.Application.Hospitalizations.Wards.Specs;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.UpdateWard;

public sealed class UpdateWardCommandHandler : IRequestHandler<UpdateWardCommand, Result<HospitalizationWardDto>>
{
    private readonly IHospitalizationWardClinicAccess _access;
    private readonly IReadRepository<HospitalizationWard> _wardsRead;
    private readonly IRepository<HospitalizationWard> _wardsWrite;
    private readonly IReadRepository<HospitalizationRoom> _roomsRead;
    private readonly TimeProvider _timeProvider;

    public UpdateWardCommandHandler(
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

    public async Task<Result<HospitalizationWardDto>> Handle(UpdateWardCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<HospitalizationWardDto>.Failure(access.Error);
        var tenantId = access.Value;

        var ward = await _wardsWrite.FirstOrDefaultAsync(
            new HospitalizationWardByIdSpec(tenantId, request.ClinicId, request.WardId), ct);
        if (ward is null)
            return Result<HospitalizationWardDto>.Failure("HospitalizationWards.NotFound", "Bölüm bulunamadı.");

        // Pasif bölüm filtreli indeksin dışındadır; ad çakışması yalnız aktif bölüm için denetlenir
        // (yeniden etkinleştirmede SetWardActive tekrar bakar).
        if (ward.IsActive)
        {
            var duplicate = await _wardsRead.AnyAsync(
                new ActiveHospitalizationWardByClinicAndNameSpec(
                    tenantId, request.ClinicId, HospitalizationWardMapping.NormalizeNameForLookup(request.Name), ward.Id),
                ct);
            if (duplicate)
            {
                return Result<HospitalizationWardDto>.Failure(
                    "HospitalizationWards.DuplicateName",
                    "Bu klinikte aynı adda aktif bir bölüm zaten var.");
            }
        }

        try
        {
            ward.Update(request.Name, request.SortOrder, _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (ArgumentException ex)
        {
            return Result<HospitalizationWardDto>.Failure("HospitalizationWards.Validation", ex.Message);
        }

        await _wardsWrite.UpdateAsync(ward, ct);
        await _wardsWrite.SaveChangesAsync(ct);

        var rooms = await _roomsRead.ListAsync(new HospitalizationRoomsByClinicSpec(tenantId, request.ClinicId), ct);
        return Result<HospitalizationWardDto>.Success(
            HospitalizationWardMapping.ToDto(ward, rooms.Where(r => r.WardId == ward.Id)));
    }
}
