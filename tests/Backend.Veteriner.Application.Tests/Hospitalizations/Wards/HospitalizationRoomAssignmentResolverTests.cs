using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Wards;
using Backend.Veteriner.Application.Hospitalizations.Wards.Specs;
using Backend.Veteriner.Domain.Hospitalizations;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Hospitalizations.Wards;

/// <summary>
/// Yatışa oda atama kuralı (Create/Update handler'larının ortak yardımcısı): oda klinikte bulunmalı, oda ve bölümü aktif
/// olmalı; mevcut yatışın zaten bağlı olduğu oda pasifleşmişse korunur.
/// </summary>
public sealed class HospitalizationRoomAssignmentResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IReadRepository<HospitalizationRoom>> _rooms = new();
    private readonly Mock<IReadRepository<HospitalizationWard>> _wards = new();

    private (HospitalizationWard Ward, HospitalizationRoom Room) SeedFound(bool wardActive = true, bool roomActive = true)
    {
        var ward = HospitalizationWard.Create(TenantId, ClinicId, "Yoğun bakım", 1, Now);
        if (!wardActive) ward.SetActive(false, Now);
        var room = HospitalizationRoom.Create(ward, "Kafes 1", 1, Now);
        if (!roomActive) room.SetActive(false, Now);
        _rooms.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationRoomByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(room);
        _wards.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationWardByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(ward);
        return (ward, room);
    }

    private Task<Backend.Veteriner.Domain.Shared.Result<HospitalizationRoomAssignment>> Resolve(Guid roomId, Guid? currentRoomId = null)
        => HospitalizationRoomAssignmentResolver.ResolveAsync(_rooms.Object, _wards.Object, TenantId, ClinicId, roomId, currentRoomId, CancellationToken.None);

    [Fact]
    public async Task Should_Return_RoomNotFound_When_Room_Not_In_Tenant_Clinic()
    {
        _rooms.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationRoomByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HospitalizationRoom?)null);

        var result = await Resolve(Guid.NewGuid());

        result.Error.Code.Should().Be("Hospitalizations.RoomNotFound");
        _wards.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_Return_RoomInactive_When_Room_Is_Passive()
    {
        var (_, room) = SeedFound(roomActive: false);

        var result = await Resolve(room.Id);

        result.Error.Code.Should().Be("Hospitalizations.RoomInactive");
    }

    [Fact]
    public async Task Should_Return_RoomInactive_When_Ward_Is_Passive_Even_If_Room_Active()
    {
        var (_, room) = SeedFound(wardActive: false);

        var result = await Resolve(room.Id);

        result.Error.Code.Should().Be("Hospitalizations.RoomInactive");
    }

    [Fact]
    public async Task Should_Keep_Current_Room_When_Resent_Even_If_Inactive()
    {
        var (ward, room) = SeedFound(roomActive: false);

        var result = await Resolve(room.Id, currentRoomId: room.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Room.Should().BeSameAs(room);
        result.Value.Ward.Should().BeSameAs(ward);
    }

    [Fact]
    public async Task Should_Succeed_For_Active_Room_In_Active_Ward()
    {
        var (ward, room) = SeedFound();

        var result = await Resolve(room.Id, currentRoomId: Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Room.Id.Should().Be(room.Id);
        result.Value.Ward.Id.Should().Be(ward.Id);
    }
}
