using Backend.Veteriner.Domain.Appointments;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Appointments;

/// <summary>Oda ve cihaz (ADR-053): tanım kuralları, kapasite hesabı ve randevudaki kaynak alanları.</summary>
public sealed class AppointmentResourceTests
{
    private static readonly DateTime T0 = new(2026, 11, 25, 7, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);

    private static AppointmentResource Resource(int capacity = 1, AppointmentResourceType type = AppointmentResourceType.Room)
        => AppointmentResource.Create(Guid.NewGuid(), Guid.NewGuid(), "  Ameliyathane ", type, capacity, 0, Now);

    private static (DateTime, DateTime) At(int fromMinute, int toMinute) => (T0.AddMinutes(fromMinute), T0.AddMinutes(toMinute));

    [Fact]
    public void Create_Should_Trim_Name_And_Start_Active()
    {
        var resource = Resource(capacity: 3);

        resource.Name.Should().Be("Ameliyathane");
        resource.Capacity.Should().Be(3);
        resource.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Create_Should_Reject_Capacity_Out_Of_Range(int capacity)
    {
        var act = () => Resource(capacity);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Should_Reject_Undefined_Type_And_Blank_Name()
    {
        ((Action)(() => Resource(type: (AppointmentResourceType)0))).Should().Throw<ArgumentException>();
        ((Action)(() => AppointmentResource.Create(Guid.NewGuid(), Guid.NewGuid(), " ", AppointmentResourceType.Room, 1, 0, Now)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_Should_Not_Change_Type()
    {
        var resource = Resource();

        var act = () => resource.Update("Ultrason", AppointmentResourceType.Equipment, 1, 0, Now);

        act.Should().Throw<ArgumentException>().WithMessage("*türü sonradan değiştirilemez*");
        resource.Update("Ameliyathane 2", AppointmentResourceType.Room, 2, 4, Now);
        resource.Name.Should().Be("Ameliyathane 2");
        resource.Capacity.Should().Be(2);
        resource.UpdatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void SetActive_Same_State_Should_Not_Count_As_Change()
    {
        var resource = Resource();

        resource.SetActive(true, Now).Should().BeFalse();
        resource.SetActive(false, Now).Should().BeTrue();
        resource.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Capacity_One_Should_Reject_Any_Overlap_But_Allow_Touching_Edges()
    {
        var resource = Resource(capacity: 1);
        var busy = new[] { At(0, 30) };

        resource.HasRoomFor(busy, T0.AddMinutes(15), T0.AddMinutes(45)).Should().BeFalse();
        resource.HasRoomFor(busy, T0.AddMinutes(30), T0.AddMinutes(60)).Should().BeTrue("biten ve başlayan randevu çakışmaz");
        resource.HasRoomFor(busy, T0.AddMinutes(-30), T0).Should().BeTrue();
        resource.HasRoomFor([], T0, T0.AddMinutes(30)).Should().BeTrue();
    }

    [Fact]
    public void Capacity_Should_Count_Peak_Concurrency_Not_Total_Overlaps()
    {
        var resource = Resource(capacity: 2);
        // 10:00-10:30 ve 10:30-11:00 aynı anda değildir: 10:00-11:00 aralığında en çok 1 kullanım var.
        var sequential = new[] { At(0, 30), At(30, 60) };
        resource.HasRoomFor(sequential, T0, T0.AddMinutes(60)).Should().BeTrue();

        var concurrent = new[] { At(0, 30), At(15, 45) };
        resource.HasRoomFor(concurrent, T0.AddMinutes(20), T0.AddMinutes(25)).Should().BeFalse();
        resource.HasRoomFor(concurrent, T0.AddMinutes(30), T0.AddMinutes(60)).Should().BeTrue("10:30 sonrası tek kullanım kalır");
    }

    [Fact]
    public void PeakUsage_Should_Clip_To_Interval_And_Ignore_Outside_Rows()
    {
        var busy = new[] { At(-60, 10), At(5, 20), At(100, 130) };

        AppointmentResource.PeakUsage(busy, T0, T0.AddMinutes(30)).Should().Be(2);
        AppointmentResource.PeakUsage(busy, T0.AddMinutes(10), T0.AddMinutes(30)).Should().Be(1);
    }

    [Fact]
    public void Appointment_Should_Carry_Room_And_Equipment_And_Reject_Empty_Ids()
    {
        var room = Guid.NewGuid();
        var device = Guid.NewGuid();
        var appointment = new Appointment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0, resources: new AppointmentResourceIds(room, device));

        appointment.RoomResourceId.Should().Be(room);
        appointment.EquipmentResourceId.Should().Be(device);
        appointment.Resources.Assigned().Should().Equal((room, AppointmentResourceType.Room), (device, AppointmentResourceType.Equipment));

        var update = appointment.UpdateDetails(
            appointment.ClinicId, appointment.PetId, T0, 30, AppointmentType.Other, null, resources: new AppointmentResourceIds(null, device));
        update.IsSuccess.Should().BeTrue();
        appointment.RoomResourceId.Should().BeNull();

        appointment.UpdateDetails(appointment.ClinicId, appointment.PetId, T0, 30, AppointmentType.Other, null, resources: new AppointmentResourceIds(Guid.Empty, null))
            .IsSuccess.Should().BeFalse();
        var act = () => new Appointment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0, resources: new AppointmentResourceIds(null, Guid.Empty));
        act.Should().Throw<ArgumentException>();
    }
}
