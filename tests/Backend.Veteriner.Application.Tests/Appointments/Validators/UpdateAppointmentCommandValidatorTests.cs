using Backend.Veteriner.Application.Appointments.Commands.Update;
using Backend.Veteriner.Application.Appointments.Commands.Update.Validators;
using Backend.Veteriner.Domain.Appointments;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace Backend.Veteriner.Application.Tests.Appointments.Validators;

public sealed class UpdateAppointmentCommandValidatorTests
{
    private readonly UpdateAppointmentCommandValidator _validator = new();

    [Fact]
    public void Should_Fail_When_DurationMinutes_Is_4()
    {
        var cmd = new UpdateAppointmentCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            AppointmentType.Examination,
            AppointmentStatus.Scheduled,
            null,
            DurationMinutes: 4);

        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.DurationMinutes);
    }

    [Fact]
    public void Should_Fail_When_DurationMinutes_Is_241()
    {
        var cmd = new UpdateAppointmentCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            AppointmentType.Examination,
            AppointmentStatus.Scheduled,
            null,
            DurationMinutes: 241);

        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.DurationMinutes);
    }
}
