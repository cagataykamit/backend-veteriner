using Backend.Veteriner.Domain.Appointments;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Appointments;

public sealed class AppointmentCancellationUndoTests
{
    private static readonly DateTime CancelledAt = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>İptal edilip kaydedilmiş randevu: <see cref="Appointment.ChangedAtUtc"/> kayıtta sunucu saatiyle yazılır.</summary>
    private static Appointment Cancelled(string? notes = null, string? reason = null)
    {
        var appointment = new Appointment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancelledAt.AddDays(1), 30, AppointmentType.Consultation, null, notes);
        appointment.Cancel(reason).IsSuccess.Should().BeTrue();
        typeof(Appointment).GetProperty(nameof(Appointment.ChangedAtUtc))!.SetValue(appointment, CancelledAt);
        return appointment;
    }

    [Fact]
    public void Window_Should_Be_Ten_Seconds_And_Limit_Should_Add_Network_Allowance()
    {
        Appointment.CancellationUndoWindow.Should().Be(TimeSpan.FromSeconds(10));
        Appointment.CancellationUndoLimit.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void RevertCancellation_Within_Limit_Should_Restore_Scheduled_And_Advance_Sequence()
    {
        var appointment = Cancelled();

        var result = appointment.RevertCancellation(CancelledAt + Appointment.CancellationUndoLimit);

        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        appointment.MutationSequence.Should().Be(2);
    }

    [Fact]
    public void RevertCancellation_After_Limit_Should_Fail_Expired()
    {
        var appointment = Cancelled();

        var result = appointment.RevertCancellation(CancelledAt + Appointment.CancellationUndoLimit + TimeSpan.FromMilliseconds(1));

        result.Error.Code.Should().Be("Appointments.CancellationUndoExpired");
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        appointment.MutationSequence.Should().Be(1);
    }

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public void RevertCancellation_Should_Reject_Not_Cancelled(AppointmentStatus status)
    {
        var appointment = new Appointment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancelledAt, 30, AppointmentType.Consultation, status);

        var result = appointment.RevertCancellation(CancelledAt);

        result.Error.Code.Should().Be("Appointments.InvalidStatusTransition");
    }

    [Theory]
    [InlineData(null, "sahibi aradı", null)]
    [InlineData("Aşı kartı getirilecek", "sahibi aradı", "Aşı kartı getirilecek")]
    [InlineData("Aşı kartı getirilecek", null, "Aşı kartı getirilecek")]
    [InlineData("Satır 1\nSatır 2", "çok satırlı\ngerekçe", "Satır 1\nSatır 2")]
    public void RevertCancellation_Should_Remove_Only_The_Cancellation_Note(string? notes, string? reason, string? expected)
    {
        var appointment = Cancelled(notes, reason);

        appointment.RevertCancellation(CancelledAt).IsSuccess.Should().BeTrue();

        appointment.Notes.Should().Be(expected);
    }
}
