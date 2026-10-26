using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Application.Hospitalizations.Wards.Access;
using Backend.Veteriner.Application.Hospitalizations.Wards.Specs;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Queries.GetClinicWardsWithRooms;

public sealed class GetClinicWardsWithRoomsQueryHandler
    : IRequestHandler<GetClinicWardsWithRoomsQuery, Result<IReadOnlyList<HospitalizationWardDto>>>
{
    private readonly IHospitalizationWardClinicAccess _access;
    private readonly IReadRepository<HospitalizationWard> _wards;
    private readonly IReadRepository<HospitalizationRoom> _rooms;

    public GetClinicWardsWithRoomsQueryHandler(
        IHospitalizationWardClinicAccess access,
        IReadRepository<HospitalizationWard> wards,
        IReadRepository<HospitalizationRoom> rooms)
    {
        _access = access;
        _wards = wards;
        _rooms = rooms;
    }

    public async ValueTask<Result<IReadOnlyList<HospitalizationWardDto>>> Handle(
        GetClinicWardsWithRoomsQuery request,
        CancellationToken ct)
    {
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<IReadOnlyList<HospitalizationWardDto>>.Failure(access.Error);
        var tenantId = access.Value;

        var wards = await _wards.ListAsync(new HospitalizationWardsByClinicSpec(tenantId, request.ClinicId), ct);
        var rooms = await _rooms.ListAsync(new HospitalizationRoomsByClinicSpec(tenantId, request.ClinicId), ct);
        var roomsByWard = rooms.ToLookup(r => r.WardId);

        IReadOnlyList<HospitalizationWardDto> dtos = wards
            .Select(w => HospitalizationWardMapping.ToDto(w, roomsByWard[w.Id]))
            .ToList();
        return Result<IReadOnlyList<HospitalizationWardDto>>.Success(dtos);
    }
}
