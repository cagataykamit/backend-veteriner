using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Wards.Access;
using Backend.Veteriner.Application.Hospitalizations.Wards.Commands.CreateRoom;
using Backend.Veteriner.Application.Hospitalizations.Wards.Commands.CreateWard;
using Backend.Veteriner.Application.Hospitalizations.Wards.Commands.SetRoomActive;
using Backend.Veteriner.Application.Hospitalizations.Wards.Commands.SetWardActive;
using Backend.Veteriner.Application.Hospitalizations.Wards.Commands.UpdateWard;
using Backend.Veteriner.Application.Hospitalizations.Wards.Queries.GetClinicWardsWithRooms;
using Backend.Veteriner.Application.Hospitalizations.Wards.Specs;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Hospitalizations.Wards;

/// <summary>
/// HOSP-002 bölüm/oda komutları: klinik erişimi tek yerde (<see cref="IHospitalizationWardClinicAccess"/>), aktif kayıtlar
/// arasında ad benzersizliği, idempotent aktif/pasif, liste sıralaması.
/// </summary>
public sealed class WardRoomCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IHospitalizationWardClinicAccess> _access = new();
    private readonly Mock<IReadRepository<HospitalizationWard>> _wardsRead = new();
    private readonly Mock<IRepository<HospitalizationWard>> _wardsWrite = new();
    private readonly Mock<IReadRepository<HospitalizationRoom>> _roomsRead = new();
    private readonly Mock<IRepository<HospitalizationRoom>> _roomsWrite = new();
    private readonly TimeProvider _clock = new FixedClock(Now);

    public WardRoomCommandHandlerTests()
    {
        _access.Setup(a => a.EnsureWriteAccessAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(TenantId));
        _access.Setup(a => a.EnsureReadAccessAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(TenantId));
        _roomsRead.Setup(r => r.ListAsync(It.IsAny<HospitalizationRoomsByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HospitalizationRoom>());
    }

    private static HospitalizationWard Ward(string name = "Yoğun bakım", bool active = true)
    {
        var ward = HospitalizationWard.Create(TenantId, ClinicId, name, 1, Now);
        if (!active) ward.SetActive(false, Now);
        return ward;
    }

    private void SetupWardFound(HospitalizationWard ward)
        => _wardsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationWardByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ward);

    private void SetupWardNameTaken(bool taken)
        => _wardsRead.Setup(r => r.AnyAsync(It.IsAny<ActiveHospitalizationWardByClinicAndNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(taken);

    // ---------------- CreateWard ----------------

    [Fact]
    public async Task CreateWard_Should_Return_Access_Error_Without_Touching_Repositories()
    {
        _access.Setup(a => a.EnsureWriteAccessAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("Auth.PermissionDenied", "yok"));

        var result = await new CreateWardCommandHandler(_access.Object, _wardsRead.Object, _wardsWrite.Object, _clock)
            .Handle(new CreateWardCommand(ClinicId, "A", 0), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
        _wardsRead.VerifyNoOtherCalls();
        _wardsWrite.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateWard_Should_Return_DuplicateName_When_Active_Ward_Has_Same_Name()
    {
        SetupWardNameTaken(true);

        var result = await new CreateWardCommandHandler(_access.Object, _wardsRead.Object, _wardsWrite.Object, _clock)
            .Handle(new CreateWardCommand(ClinicId, " yoğun bakım ", 0), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("HospitalizationWards.DuplicateName");
        _wardsWrite.Verify(r => r.AddAsync(It.IsAny<HospitalizationWard>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateWard_Should_Persist_Trimmed_Name_With_Tenant_From_Access()
    {
        SetupWardNameTaken(false);
        HospitalizationWard? added = null;
        _wardsWrite.Setup(r => r.AddAsync(It.IsAny<HospitalizationWard>(), It.IsAny<CancellationToken>()))
            .Callback<HospitalizationWard, CancellationToken>((w, _) => added = w)
            .ReturnsAsync((HospitalizationWard w, CancellationToken _) => w);

        var result = await new CreateWardCommandHandler(_access.Object, _wardsRead.Object, _wardsWrite.Object, _clock)
            .Handle(new CreateWardCommand(ClinicId, " Kedi servisi ", 3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.Id.Should().Be(result.Value);
        added.TenantId.Should().Be(TenantId);
        added.ClinicId.Should().Be(ClinicId);
        added.Name.Should().Be("Kedi servisi");
        added.SortOrder.Should().Be(3);
        added.CreatedAtUtc.Should().Be(Now);
        _wardsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------- UpdateWard ----------------

    [Fact]
    public async Task UpdateWard_Should_Return_NotFound_When_Ward_Missing_In_Clinic()
    {
        _wardsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationWardByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HospitalizationWard?)null);

        var result = await new UpdateWardCommandHandler(_access.Object, _wardsRead.Object, _wardsWrite.Object, _roomsRead.Object, _clock)
            .Handle(new UpdateWardCommand(ClinicId, Guid.NewGuid(), "A", 0), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("HospitalizationWards.NotFound");
    }

    [Fact]
    public async Task UpdateWard_Should_Skip_Duplicate_Check_For_Inactive_Ward_And_Rename()
    {
        var ward = Ward(active: false);
        SetupWardFound(ward);

        var result = await new UpdateWardCommandHandler(_access.Object, _wardsRead.Object, _wardsWrite.Object, _roomsRead.Object, _clock)
            .Handle(new UpdateWardCommand(ClinicId, ward.Id, "Yeni ad", 9), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Yeni ad");
        result.Value.IsActive.Should().BeFalse();
        _wardsRead.Verify(r => r.AnyAsync(It.IsAny<ActiveHospitalizationWardByClinicAndNameSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _wardsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateWard_Should_Return_DuplicateName_For_Active_Ward()
    {
        var ward = Ward();
        SetupWardFound(ward);
        SetupWardNameTaken(true);

        var result = await new UpdateWardCommandHandler(_access.Object, _wardsRead.Object, _wardsWrite.Object, _roomsRead.Object, _clock)
            .Handle(new UpdateWardCommand(ClinicId, ward.Id, "Kedi servisi", 0), CancellationToken.None);

        result.Error.Code.Should().Be("HospitalizationWards.DuplicateName");
        ward.Name.Should().Be("Yoğun bakım");
    }

    // ---------------- SetWardActive ----------------

    [Fact]
    public async Task SetWardActive_Should_Be_Idempotent_And_Not_Save_When_State_Unchanged()
    {
        var ward = Ward();
        SetupWardFound(ward);

        var result = await new SetWardActiveCommandHandler(_access.Object, _wardsRead.Object, _wardsWrite.Object, _roomsRead.Object, _clock)
            .Handle(new SetWardActiveCommand(ClinicId, ward.Id, IsActive: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsActive.Should().BeTrue();
        _wardsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetWardActive_Should_Reject_Reactivation_When_Name_Taken_By_Active_Ward()
    {
        var ward = Ward(active: false);
        SetupWardFound(ward);
        SetupWardNameTaken(true);

        var result = await new SetWardActiveCommandHandler(_access.Object, _wardsRead.Object, _wardsWrite.Object, _roomsRead.Object, _clock)
            .Handle(new SetWardActiveCommand(ClinicId, ward.Id, IsActive: true), CancellationToken.None);

        result.Error.Code.Should().Be("HospitalizationWards.DuplicateName");
        ward.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SetWardActive_Should_Deactivate_And_Return_Ward_With_Its_Rooms_Only()
    {
        var ward = Ward();
        var otherWard = Ward("Diğer");
        SetupWardFound(ward);
        _roomsRead.Setup(r => r.ListAsync(It.IsAny<HospitalizationRoomsByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HospitalizationRoom>
            {
                HospitalizationRoom.Create(ward, "Kafes 2", 2, Now),
                HospitalizationRoom.Create(ward, "Kafes 1", 1, Now),
                HospitalizationRoom.Create(otherWard, "Başka", 0, Now),
            });

        var result = await new SetWardActiveCommandHandler(_access.Object, _wardsRead.Object, _wardsWrite.Object, _roomsRead.Object, _clock)
            .Handle(new SetWardActiveCommand(ClinicId, ward.Id, IsActive: false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsActive.Should().BeFalse();
        result.Value.Rooms.Select(r => r.Name).Should().Equal("Kafes 1", "Kafes 2");
        _wardsRead.Verify(r => r.AnyAsync(It.IsAny<ActiveHospitalizationWardByClinicAndNameSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _wardsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------- CreateRoom / SetRoomActive ----------------

    [Fact]
    public async Task CreateRoom_Should_Return_WardNotFound_When_Ward_Missing()
    {
        _wardsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationWardByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HospitalizationWard?)null);

        var result = await new CreateRoomCommandHandler(_access.Object, _wardsRead.Object, _roomsRead.Object, _roomsWrite.Object, _clock)
            .Handle(new CreateRoomCommand(ClinicId, Guid.NewGuid(), "Kafes 1", 0), CancellationToken.None);

        result.Error.Code.Should().Be("HospitalizationWards.NotFound");
        _roomsWrite.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateRoom_Should_Return_DuplicateName_When_Active_Room_Exists_In_Ward()
    {
        var ward = Ward();
        _wardsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationWardByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ward);
        _roomsRead.Setup(r => r.AnyAsync(It.IsAny<ActiveHospitalizationRoomByWardAndNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await new CreateRoomCommandHandler(_access.Object, _wardsRead.Object, _roomsRead.Object, _roomsWrite.Object, _clock)
            .Handle(new CreateRoomCommand(ClinicId, ward.Id, "Kafes 1", 0), CancellationToken.None);

        result.Error.Code.Should().Be("HospitalizationRooms.DuplicateName");
    }

    [Fact]
    public async Task CreateRoom_Should_Persist_Room_Bound_To_Ward()
    {
        var ward = Ward();
        _wardsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationWardByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ward);
        _roomsRead.Setup(r => r.AnyAsync(It.IsAny<ActiveHospitalizationRoomByWardAndNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        HospitalizationRoom? added = null;
        _roomsWrite.Setup(r => r.AddAsync(It.IsAny<HospitalizationRoom>(), It.IsAny<CancellationToken>()))
            .Callback<HospitalizationRoom, CancellationToken>((r, _) => added = r)
            .ReturnsAsync((HospitalizationRoom r, CancellationToken _) => r);

        var result = await new CreateRoomCommandHandler(_access.Object, _wardsRead.Object, _roomsRead.Object, _roomsWrite.Object, _clock)
            .Handle(new CreateRoomCommand(ClinicId, ward.Id, "Kafes 1", 4), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.WardId.Should().Be(ward.Id);
        added.ClinicId.Should().Be(ClinicId);
        added.TenantId.Should().Be(TenantId);
        added.Id.Should().Be(result.Value);
    }

    [Fact]
    public async Task SetRoomActive_Should_Return_NotFound_When_Room_Not_In_Ward()
    {
        _roomsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationRoomByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HospitalizationRoom?)null);

        var result = await new SetRoomActiveCommandHandler(_access.Object, _roomsRead.Object, _roomsWrite.Object, _clock)
            .Handle(new SetRoomActiveCommand(ClinicId, Guid.NewGuid(), Guid.NewGuid(), IsActive: false), CancellationToken.None);

        result.Error.Code.Should().Be("HospitalizationRooms.NotFound");
    }

    [Fact]
    public async Task SetRoomActive_Should_Deactivate_Room()
    {
        var room = HospitalizationRoom.Create(Ward(), "Kafes 1", 1, Now);
        _roomsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationRoomByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(room);

        var result = await new SetRoomActiveCommandHandler(_access.Object, _roomsRead.Object, _roomsWrite.Object, _clock)
            .Handle(new SetRoomActiveCommand(ClinicId, room.WardId, room.Id, IsActive: false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsActive.Should().BeFalse();
        _roomsRead.Verify(r => r.AnyAsync(It.IsAny<ActiveHospitalizationRoomByWardAndNameSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _roomsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------- GetClinicWardsWithRooms ----------------

    [Fact]
    public async Task GetClinicWardsWithRooms_Should_Group_Rooms_Under_Wards_Including_Inactive()
    {
        var icu = Ward("Yoğun bakım");
        var cats = Ward("Kedi servisi", active: false);
        _wardsRead.Setup(r => r.ListAsync(It.IsAny<HospitalizationWardsByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HospitalizationWard> { icu, cats });
        _roomsRead.Setup(r => r.ListAsync(It.IsAny<HospitalizationRoomsByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HospitalizationRoom>
            {
                HospitalizationRoom.Create(icu, "Kafes 1", 1, Now),
                HospitalizationRoom.Create(cats, "Oda A", 1, Now),
            });

        var result = await new GetClinicWardsWithRoomsQueryHandler(_access.Object, _wardsRead.Object, _roomsRead.Object)
            .Handle(new GetClinicWardsWithRoomsQuery(ClinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value[0].Rooms.Single().Name.Should().Be("Kafes 1");
        result.Value[1].IsActive.Should().BeFalse();
        result.Value[1].Rooms.Single().WardId.Should().Be(cats.Id);
    }

    [Fact]
    public async Task GetClinicWardsWithRooms_Should_Return_Access_Error()
    {
        _access.Setup(a => a.EnsureReadAccessAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("Clinics.AccessDenied", "yok"));

        var result = await new GetClinicWardsWithRoomsQueryHandler(_access.Object, _wardsRead.Object, _roomsRead.Object)
            .Handle(new GetClinicWardsWithRoomsQuery(ClinicId), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _wardsRead.VerifyNoOtherCalls();
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
