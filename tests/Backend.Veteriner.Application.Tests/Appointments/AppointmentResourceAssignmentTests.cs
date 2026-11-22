using Backend.Veteriner.Application.Appointments;
using Backend.Veteriner.Application.Appointments.Resources;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.Appointments;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments;

/// <summary>Randevuda oda ve cihaz kuralı (ADR-053 K3/K4; <see cref="AppointmentAssignment.EnsureResourcesAsync"/>).</summary>
public sealed class AppointmentResourceAssignmentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 11, 25, 7, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IReadRepository<AppointmentResource>> _resources = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();

    private AppointmentAssignment Assignment() => new(
        new Mock<IReadRepository<AppointmentReason>>().Object,
        _appointments.Object,
        new Mock<IClinicVeterinarianReader>().Object,
        new Mock<IUserNotificationPublisher>().Object,
        new Mock<IClientContext>().Object,
        new Mock<ITransactionLock>().Object,
        _resources.Object);

    private AppointmentResource Given(AppointmentResourceType type, int capacity = 1, bool active = true, params (int From, int To)[] busy)
    {
        var resource = AppointmentResource.Create(TenantId, ClinicId, type == AppointmentResourceType.Room ? "Ameliyathane" : "Ultrason", type, capacity, 0, Now);
        if (!active)
            resource.SetActive(false, Now);

        _resources.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentResourceByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(resource);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentIntervalsUsingResourceSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(busy.Select(b => new AppointmentInterval(Start.AddMinutes(b.From), b.To - b.From)).ToList());
        return resource;
    }

    private Task<Backend.Veteriner.Domain.Shared.Result> Ensure(AppointmentResourceIds resources, AppointmentResourceIds keep = default)
        => Assignment().EnsureResourcesAsync(TenantId, ClinicId, resources, keep, Start, Start.AddMinutes(30), null, CancellationToken.None);

    [Fact]
    public async Task No_Resources_Should_Succeed_Without_Queries()
    {
        (await Ensure(default)).IsSuccess.Should().BeTrue();
        _resources.VerifyNoOtherCalls();
        _appointments.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Free_Room_Should_Succeed()
    {
        var room = Given(AppointmentResourceType.Room, busy: (30, 60));

        (await Ensure(new AppointmentResourceIds(room.Id, null))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Busy_Room_Should_Fail_With_ResourceTimeConflict()
    {
        var room = Given(AppointmentResourceType.Room, busy: (15, 45));

        var result = await Ensure(new AppointmentResourceIds(room.Id, null));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.ResourceTimeConflict");
        result.Error.Message.Should().Contain("Ameliyathane");
    }

    [Fact]
    public async Task Capacity_Two_Should_Allow_One_Concurrent_Appointment()
    {
        var room = Given(AppointmentResourceType.Room, capacity: 2, busy: (0, 30));

        (await Ensure(new AppointmentResourceIds(room.Id, null))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_Resource_Should_Fail_NotFound()
    {
        _resources.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentResourceByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppointmentResource?)null);

        var result = await Ensure(new AppointmentResourceIds(Guid.NewGuid(), null));

        result.Error.Code.Should().Be("AppointmentResources.NotFound");
    }

    [Fact]
    public async Task Equipment_In_Room_Field_Should_Fail_Validation()
    {
        var device = Given(AppointmentResourceType.Equipment);

        var result = await Ensure(new AppointmentResourceIds(device.Id, null));

        result.Error.Code.Should().Be("Appointments.Validation");
        result.Error.Message.Should().Contain("Oda alanında");
    }

    [Fact]
    public async Task Inactive_Resource_Should_Be_Rejected_Unless_Kept_From_Existing_Appointment()
    {
        var device = Given(AppointmentResourceType.Equipment, active: false);
        var ids = new AppointmentResourceIds(null, device.Id);

        (await Ensure(ids)).Error.Code.Should().Be("Appointments.Validation");
        (await Ensure(ids, keep: ids)).IsSuccess.Should().BeTrue();
    }
}
