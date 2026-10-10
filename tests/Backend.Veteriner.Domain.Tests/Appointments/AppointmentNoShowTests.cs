using Backend.Veteriner.Domain.Appointments;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Appointments;

public sealed class AppointmentNoShowTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static Appointment Create(DateTime scheduledAtUtc, AppointmentStatus? status = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), scheduledAtUtc, 30, AppointmentType.Consultation, status);

    [Fact]
    public void MarkNoShow_Should_Set_Status_Append_Reason_And_Advance_Sequence()
    {
        var appointment = Create(Now.AddHours(-2));

        var result = appointment.MarkNoShow(Now, "Aranmadi");

        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.NoShow);
        appointment.Notes.Should().Be("Gelmedi: Aranmadi");
        appointment.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void MarkNoShow_Should_Be_Idempotent_Without_Sequence_Change()
    {
        var appointment = Create(Now.AddHours(-2));
        appointment.MarkNoShow(Now);

        var result = appointment.MarkNoShow(Now);

        result.IsSuccess.Should().BeTrue();
        appointment.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void MarkNoShow_Should_Reject_Future_Appointment()
    {
        var appointment = Create(Now.AddMinutes(1));

        var result = appointment.MarkNoShow(Now);

        result.Error.Code.Should().Be("Appointments.NoShowNotYetDue");
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        appointment.MutationSequence.Should().Be(0);
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public void MarkNoShow_Should_Reject_Terminal_Appointment(AppointmentStatus status)
    {
        var appointment = Create(Now.AddHours(-2), status);

        appointment.MarkNoShow(Now).Error.Code.Should().Be("Appointments.InvalidStatusTransition");
        appointment.Status.Should().Be(status);
    }

    [Fact]
    public void NoShow_Should_Be_Terminal_For_Other_Transitions()
    {
        var appointment = Create(Now.AddHours(-2));
        appointment.MarkNoShow(Now);

        appointment.Cancel("x").Error.Code.Should().Be("Appointments.InvalidStatusTransition");
        appointment.Complete().Error.Code.Should().Be("Appointments.InvalidStatusTransition");
        appointment.RescheduleTo(Now.AddDays(1)).Error.Code.Should().Be("Appointments.InvalidStatusTransition");
        appointment.EnsureCanApplyStatus(AppointmentStatus.Scheduled).Error.Code
            .Should().Be("Appointments.InvalidStatusTransition");
    }

    [Fact]
    public void RevertNoShow_Should_Return_To_Scheduled_With_Reason()
    {
        var appointment = Create(Now.AddHours(-2));
        appointment.MarkNoShow(Now);

        var result = appointment.RevertNoShow("Yanlis isaretlendi");

        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        appointment.Notes.Should().Contain("Gelmedi geri alındı: Yanlis isaretlendi");
        appointment.MutationSequence.Should().Be(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("abc")]
    public void RevertNoShow_Should_Require_Reason(string? reason)
    {
        var appointment = Create(Now.AddHours(-2));
        appointment.MarkNoShow(Now);

        appointment.RevertNoShow(reason).Error.Code.Should().Be("Appointments.Validation");
        appointment.Status.Should().Be(AppointmentStatus.NoShow);
    }

    [Fact]
    public void RevertNoShow_Should_Reject_When_Not_NoShow()
    {
        var appointment = Create(Now.AddHours(-2));

        appointment.RevertNoShow("Yanlis isaretlendi").Error.Code.Should().Be("Appointments.InvalidStatusTransition");
        appointment.RevertNoShowOnArrival().Error.Code.Should().Be("Appointments.InvalidStatusTransition");
    }

    [Fact]
    public void RevertNoShowOnArrival_Should_Return_To_Scheduled_Without_Reason()
    {
        var appointment = Create(Now.AddHours(-2));
        appointment.MarkNoShow(Now);

        appointment.RevertNoShowOnArrival().IsSuccess.Should().BeTrue();

        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        appointment.MutationSequence.Should().Be(2);
    }
}
