using Backend.Veteriner.Domain.Appointments;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Appointments;

public sealed class AppointmentMutationSequenceTests
{
    private static Appointment CreateScheduled()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(2),
            30,
            AppointmentType.Consultation);

    [Fact]
    public void NewAppointment_Should_Start_With_Zero_Sequence()
    {
        var appointment = CreateScheduled();
        appointment.MutationSequence.Should().Be(0);
    }

    [Fact]
    public void AdvanceMutationSequence_Should_Produce_First_Sequence_One()
    {
        var appointment = CreateScheduled();
        appointment.AdvanceMutationSequence().Should().Be(1);
        appointment.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void UpdateDetails_Should_Advance_Sequence_Once()
    {
        var appointment = CreateScheduled();
        var result = appointment.UpdateDetails(
            appointment.ClinicId,
            appointment.PetId,
            appointment.ScheduledAtUtc.AddHours(1),
            45,
            AppointmentType.Vaccination,
            "updated");

        result.IsSuccess.Should().BeTrue();
        appointment.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void RescheduleTo_Should_Advance_Sequence_Once()
    {
        var appointment = CreateScheduled();
        var result = appointment.RescheduleTo(appointment.ScheduledAtUtc.AddDays(1));

        result.IsSuccess.Should().BeTrue();
        appointment.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void Cancel_Should_Advance_Sequence_Once()
    {
        var appointment = CreateScheduled();
        var result = appointment.Cancel("reason");

        result.IsSuccess.Should().BeTrue();
        appointment.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void Complete_Should_Advance_Sequence_Once()
    {
        var appointment = CreateScheduled();
        var result = appointment.Complete();

        result.IsSuccess.Should().BeTrue();
        appointment.MutationSequence.Should().Be(1);
    }

    [Fact]
    public void ValidationFailure_Should_Not_Advance_Sequence()
    {
        var appointment = CreateScheduled();
        appointment.Complete();
        var before = appointment.MutationSequence;

        var result = appointment.RescheduleTo(DateTime.UtcNow.AddDays(3));

        result.IsSuccess.Should().BeFalse();
        appointment.MutationSequence.Should().Be(before);
    }

    [Fact]
    public void TerminalNoOpUpdate_Should_Not_Advance_Sequence()
    {
        var appointment = new Appointment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(2),
            30,
            AppointmentType.Consultation,
            AppointmentStatus.Completed);
        appointment.AdvanceMutationSequence();

        var result = appointment.ApplyWriteUpdate(
            AppointmentStatus.Completed,
            appointment.ClinicId,
            appointment.PetId,
            appointment.ScheduledAtUtc,
            appointment.DurationMinutes,
            appointment.AppointmentType,
            appointment.Notes);

        result.IsSuccess.Should().BeTrue();
        appointment.MutationSequence.Should().Be(1);
    }
}
