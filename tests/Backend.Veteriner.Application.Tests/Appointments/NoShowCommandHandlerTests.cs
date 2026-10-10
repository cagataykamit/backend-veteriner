using Backend.Veteriner.Application.Appointments.Commands.NoShow;
using Backend.Veteriner.Application.Appointments.Commands.RevertNoShow;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Visits;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments;

public sealed class NoShowCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Appointment>> _appointmentsRead = new();
    private readonly Mock<IRepository<Appointment>> _appointmentsWrite = new();
    private readonly Mock<IReadRepository<Visit>> _visits = new();
    private readonly Mock<IAppointmentProjectionSnapshotFactory> _snapshotFactory = new();
    private readonly Mock<IAppointmentIntegrationEventOutbox> _eventOutbox = new();

    public NoShowCommandHandlerTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        AppointmentHandlerOutboxTestSupport.SetupDefaultOutboxMocks(_snapshotFactory, _eventOutbox);
    }

    private MarkAppointmentNoShowCommandHandler CreateMarkHandler()
        => new(
            _tenantContext.Object, _clinicContext.Object, _scopeResolver.Object,
            _appointmentsRead.Object, _appointmentsWrite.Object, _visits.Object,
            _snapshotFactory.Object, _eventOutbox.Object, new FixedClock(Now));

    private RevertAppointmentNoShowCommandHandler CreateRevertHandler()
        => new(
            _tenantContext.Object, _clinicContext.Object, _scopeResolver.Object,
            _appointmentsRead.Object, _appointmentsWrite.Object, _snapshotFactory.Object, _eventOutbox.Object);

    private Appointment Returns(DateTime scheduledAtUtc, AppointmentStatus? status = null)
    {
        var appointment = new Appointment(
            _tenantId, _clinicId, Guid.NewGuid(), scheduledAtUtc, 30, AppointmentType.Other, status, null);
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment);
        return appointment;
    }

    private void VerifyEnqueued(Times times)
        => _eventOutbox.Verify(o => o.EnqueueAsync(
            AppointmentIntegrationEventTypes.Updated,
            It.IsAny<AppointmentUpdatedIntegrationEvent>(),
            It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task Mark_Should_Set_NoShow_Enqueue_Updated_Event_And_Save()
    {
        var appointment = Returns(Now.AddHours(-2));

        var result = await CreateMarkHandler().Handle(
            new MarkAppointmentNoShowCommand(appointment.Id, "Aranmadi"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.NoShow);
        VerifyEnqueued(Times.Once());
        _appointmentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Mark_Should_Be_Idempotent_Without_Event_Or_Save_When_Already_NoShow()
    {
        var appointment = Returns(Now.AddHours(-2), AppointmentStatus.NoShow);

        var result = await CreateMarkHandler().Handle(
            new MarkAppointmentNoShowCommand(appointment.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        VerifyEnqueued(Times.Never());
        _appointmentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Mark_Should_Fail_For_Future_Appointment_Without_Side_Effects()
    {
        var appointment = Returns(Now.AddHours(3));

        var result = await CreateMarkHandler().Handle(
            new MarkAppointmentNoShowCommand(appointment.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Appointments.NoShowNotYetDue");
        VerifyEnqueued(Times.Never());
        _appointmentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Mark_Should_Fail_HasVisit_When_Patient_Already_Arrived()
    {
        var appointment = Returns(Now.AddHours(-2));
        _visits.Setup(v => v.AnyAsync(It.IsAny<VisitByAppointmentIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateMarkHandler().Handle(
            new MarkAppointmentNoShowCommand(appointment.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Appointments.HasVisit");
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task Mark_Should_Fail_For_Terminal_Appointment(AppointmentStatus status)
    {
        var appointment = Returns(Now.AddHours(-2), status);

        var result = await CreateMarkHandler().Handle(
            new MarkAppointmentNoShowCommand(appointment.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Appointments.InvalidStatusTransition");
    }

    [Fact]
    public async Task Mark_And_Revert_Should_Return_NotFound_For_Missing_Or_Other_Clinic_Appointment()
    {
        _appointmentsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
        (await CreateMarkHandler().Handle(new MarkAppointmentNoShowCommand(Guid.NewGuid()), CancellationToken.None))
            .Error.Code.Should().Be("Appointments.NotFound");
        (await CreateRevertHandler().Handle(new RevertAppointmentNoShowCommand(Guid.NewGuid(), "Yanlis isaret"), CancellationToken.None))
            .Error.Code.Should().Be("Appointments.NotFound");

        var appointment = Returns(Now.AddHours(-2));
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        (await CreateMarkHandler().Handle(new MarkAppointmentNoShowCommand(appointment.Id), CancellationToken.None))
            .Error.Code.Should().Be("Appointments.NotFound");
    }

    [Fact]
    public async Task Mark_And_Revert_Should_Return_AccessDenied_When_Clinic_Not_Assigned()
    {
        var appointment = Returns(Now.AddHours(-2));
        _scopeResolver.SetupAccessDenied();

        (await CreateMarkHandler().Handle(new MarkAppointmentNoShowCommand(appointment.Id), CancellationToken.None))
            .Error.Code.Should().Be("Clinics.AccessDenied");
        (await CreateRevertHandler().Handle(new RevertAppointmentNoShowCommand(appointment.Id, "Yanlis isaret"), CancellationToken.None))
            .Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Mark_Should_Return_ConcurrencyConflict_When_Save_Races()
    {
        var appointment = Returns(Now.AddHours(-2));
        _appointmentsWrite.Setup(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateMarkHandler().Handle(
            new MarkAppointmentNoShowCommand(appointment.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Appointments.ConcurrencyConflict");
    }

    [Fact]
    public async Task Revert_Should_Return_To_Scheduled_Enqueue_Event_And_Save()
    {
        var appointment = Returns(Now.AddHours(-2), AppointmentStatus.NoShow);

        var result = await CreateRevertHandler().Handle(
            new RevertAppointmentNoShowCommand(appointment.Id, "Yanlis isaretlendi"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        VerifyEnqueued(Times.Once());
        _appointmentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revert_Should_Require_Reason_And_NoShow_Status_Without_Side_Effects()
    {
        var noShow = Returns(Now.AddHours(-2), AppointmentStatus.NoShow);
        (await CreateRevertHandler().Handle(new RevertAppointmentNoShowCommand(noShow.Id, " "), CancellationToken.None))
            .Error.Code.Should().Be("Appointments.Validation");

        var scheduled = Returns(Now.AddHours(-2));
        (await CreateRevertHandler().Handle(new RevertAppointmentNoShowCommand(scheduled.Id, "Yanlis isaretlendi"), CancellationToken.None))
            .Error.Code.Should().Be("Appointments.InvalidStatusTransition");

        VerifyEnqueued(Times.Never());
        _appointmentsWrite.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Commands_Should_Expose_Audit_Action_And_Target()
    {
        var id = Guid.NewGuid();
        var mark = new MarkAppointmentNoShowCommand(id);
        var revert = new RevertAppointmentNoShowCommand(id, "Yanlis isaretlendi");

        mark.AuditAction.Should().Be("Appointment.NoShow");
        revert.AuditAction.Should().Be("Appointment.NoShowRevert");
        mark.AuditTarget.Should().Be($"AppointmentId={id}");
        revert.AuditTarget.Should().Be($"AppointmentId={id}");
        mark.Should().BeAssignableTo<IAuditableRequest>();
        revert.Should().BeAssignableTo<IAuditableRequest>();
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
