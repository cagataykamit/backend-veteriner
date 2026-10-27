using Backend.Veteriner.Domain.Hospitalizations;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Hospitalizations;

/// <summary>HOSP-002: bölüm/oda tanımları ve yatışa oda ataması (konum metni odadan türetilir).</summary>
public sealed class HospitalizationWardRoomTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static HospitalizationWard Ward(string name = "Yoğun bakım", int sortOrder = 1)
        => HospitalizationWard.Create(TenantId, ClinicId, name, sortOrder, Now);

    private static Hospitalization Stay(Guid? clinicId = null)
        => new(TenantId, clinicId ?? ClinicId, Guid.NewGuid(), null, Now.AddHours(-3), null, "Gözlem", null, Now, null, "Kafes 3");

    [Fact]
    public void Ward_Create_Should_Trim_Name_And_Start_Active()
    {
        var ward = Ward("  Kedi servisi ", 5);

        ward.Name.Should().Be("Kedi servisi");
        ward.SortOrder.Should().Be(5);
        ward.IsActive.Should().BeTrue();
        ward.TenantId.Should().Be(TenantId);
        ward.ClinicId.Should().Be(ClinicId);
        ward.CreatedAtUtc.Should().Be(Now);
        ward.UpdatedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ward_Create_Should_Reject_Blank_Name(string name)
    {
        var act = () => HospitalizationWard.Create(TenantId, ClinicId, name, 0, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ward_Create_Should_Reject_Name_Over_Max_Length()
    {
        var act = () => HospitalizationWard.Create(TenantId, ClinicId, new string('a', HospitalizationWard.NameMaxLength + 1), 0, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ward_Create_Should_Reject_Empty_Tenant_Or_Clinic()
    {
        var noTenant = () => HospitalizationWard.Create(Guid.Empty, ClinicId, "A", 0, Now);
        var noClinic = () => HospitalizationWard.Create(TenantId, Guid.Empty, "A", 0, Now);

        noTenant.Should().Throw<ArgumentException>();
        noClinic.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ward_SetActive_Should_Report_Change_Only_When_State_Differs()
    {
        var ward = Ward();
        var later = Now.AddMinutes(5);

        ward.SetActive(true, later).Should().BeFalse();
        ward.UpdatedAtUtc.Should().BeNull();

        ward.SetActive(false, later).Should().BeTrue();
        ward.IsActive.Should().BeFalse();
        ward.UpdatedAtUtc.Should().Be(later);
    }

    [Fact]
    public void Room_Create_Should_Inherit_Tenant_Clinic_And_Ward_From_Ward()
    {
        var ward = Ward();

        var room = HospitalizationRoom.Create(ward, " Kafes 1 ", 2, Now);

        room.TenantId.Should().Be(ward.TenantId);
        room.ClinicId.Should().Be(ward.ClinicId);
        room.WardId.Should().Be(ward.Id);
        room.Name.Should().Be("Kafes 1");
        room.SortOrder.Should().Be(2);
        room.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Room_Update_Should_Rename_And_Stamp_UpdatedAt()
    {
        var room = HospitalizationRoom.Create(Ward(), "Kafes 1", 1, Now);
        var later = Now.AddMinutes(1);

        room.Update(" Kafes 1B ", 7, later);

        room.Name.Should().Be("Kafes 1B");
        room.SortOrder.Should().Be(7);
        room.UpdatedAtUtc.Should().Be(later);
    }

    [Fact]
    public void AssignRoom_Should_Set_RoomId_And_Derive_Location_From_Ward_And_Room()
    {
        var ward = Ward("Yoğun bakım");
        var room = HospitalizationRoom.Create(ward, "Kafes 2", 1, Now);
        var stay = Stay();

        var result = stay.AssignRoom(room, ward);

        result.IsSuccess.Should().BeTrue();
        stay.RoomId.Should().Be(room.Id);
        stay.Location.Should().Be("Yoğun bakım / Kafes 2");
        Hospitalization.FormatRoomLocation("A", "B").Should().Be("A / B");
    }

    [Fact]
    public void AssignRoom_Should_Reject_Room_From_Another_Ward_Or_Clinic()
    {
        var ward = Ward();
        var otherWard = Ward("Diğer");
        var room = HospitalizationRoom.Create(ward, "Kafes 2", 1, Now);

        var wrongWard = () => Stay().AssignRoom(room, otherWard);
        var wrongClinic = () => Stay(Guid.NewGuid()).AssignRoom(room, ward);

        wrongWard.Should().Throw<ArgumentException>();
        wrongClinic.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AssignRoom_And_ClearRoom_Should_Fail_When_Discharged()
    {
        var ward = Ward();
        var room = HospitalizationRoom.Create(ward, "Kafes 2", 1, Now);
        var stay = Stay();
        stay.Discharge(Now, null, Now).IsSuccess.Should().BeTrue();

        stay.AssignRoom(room, ward).Error.Code.Should().Be("Hospitalizations.AlreadyDischarged");
        stay.ClearRoom().Error.Code.Should().Be("Hospitalizations.AlreadyDischarged");
    }

    [Fact]
    public void ClearRoom_Should_Remove_Link_But_Keep_Location_Text()
    {
        var ward = Ward();
        var room = HospitalizationRoom.Create(ward, "Kafes 2", 1, Now);
        var stay = Stay();
        stay.AssignRoom(room, ward).IsSuccess.Should().BeTrue();

        stay.ClearRoom().IsSuccess.Should().BeTrue();

        stay.RoomId.Should().BeNull();
        stay.Location.Should().Be("Yoğun bakım / Kafes 2");
    }
}
