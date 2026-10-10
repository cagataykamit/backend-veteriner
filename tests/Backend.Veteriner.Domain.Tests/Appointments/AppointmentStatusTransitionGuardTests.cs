using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Appointments;

/// <summary>
/// Update/Create akışı durum geçişi yaptıramaz (izin ayrımı): tamamlama, iptal ve gelmedi yalnızca kendi uçlarıyla.
/// </summary>
public sealed class AppointmentStatusTransitionGuardTests
{
    private static Appointment CreateScheduled()
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(2), 30, AppointmentType.Consultation);

    private static Result Apply(Appointment a, AppointmentStatus requested)
        => a.ApplyWriteUpdate(requested, a.ClinicId, a.PetId, a.ScheduledAtUtc, a.DurationMinutes, a.AppointmentType, "not");

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public void ApplyWriteUpdate_Should_Reject_Status_Change_And_Leave_Appointment_Untouched(AppointmentStatus requested)
    {
        var appointment = CreateScheduled();

        var result = Apply(appointment, requested);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.InvalidStatusTransition");
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        appointment.Notes.Should().BeNull();
        appointment.MutationSequence.Should().Be(0);
    }

    [Fact]
    public void ApplyWriteUpdate_Should_Update_Details_When_Status_Unchanged()
    {
        var appointment = CreateScheduled();

        Apply(appointment, AppointmentStatus.Scheduled).IsSuccess.Should().BeTrue();

        appointment.Notes.Should().Be("not");
        appointment.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void EnsureCanApplyStatus_Should_Reject_Undefined_And_Changed_Status()
    {
        var appointment = CreateScheduled();

        appointment.EnsureCanApplyStatus((AppointmentStatus)99).Error.Code.Should().Be("Appointments.Validation");
        appointment.EnsureCanApplyStatus(AppointmentStatus.Cancelled).Error.Code
            .Should().Be("Appointments.InvalidStatusTransition");
        appointment.EnsureCanApplyStatus(AppointmentStatus.Scheduled).IsSuccess.Should().BeTrue();
    }
}
