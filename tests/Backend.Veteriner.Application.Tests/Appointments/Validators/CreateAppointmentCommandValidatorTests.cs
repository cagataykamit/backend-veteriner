using Backend.Veteriner.Application.Appointments.Commands.Create;
using Backend.Veteriner.Application.Appointments.Commands.Create.Validators;
using Backend.Veteriner.Domain.Appointments;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace Backend.Veteriner.Application.Tests.Appointments.Validators;

public sealed class CreateAppointmentCommandValidatorTests
{
    private readonly CreateAppointmentCommandValidator _validator = new();

    [Fact]
    public void Should_Fail_When_DurationMinutes_Is_4()
    {
        var cmd = new CreateAppointmentCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            AppointmentType.Examination,
            null,
            null,
            DurationMinutes: 4);

        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.DurationMinutes);
    }

    [Fact]
    public void Should_Fail_When_DurationMinutes_Is_241()
    {
        var cmd = new CreateAppointmentCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            AppointmentType.Examination,
            null,
            null,
            DurationMinutes: 241);

        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.DurationMinutes);
    }

    [Fact]
    public void Should_Pass_When_DurationMinutes_Is_5_Or_240()
    {
        var cmd5 = new CreateAppointmentCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            AppointmentType.Examination,
            null,
            null,
            DurationMinutes: 5);
        _validator.Validate(cmd5).IsValid.Should().BeTrue();

        var cmd240 = cmd5 with { DurationMinutes = 240 };
        _validator.Validate(cmd240).IsValid.Should().BeTrue();
    }
}
