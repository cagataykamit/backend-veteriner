using Backend.Veteriner.Application.Reminders.Commands.UpdateSettings;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Reminders.Validators;

public sealed class UpdateReminderSettingsCommandValidatorTests
{
    private readonly UpdateReminderSettingsCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Fail_When_AppointmentReminderHoursBefore_OutOfRange()
    {
        var cmd = new UpdateReminderSettingsCommand(true, 0, false, 3, true);
        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpdateReminderSettingsCommand.AppointmentReminderHoursBefore));
    }

    [Fact]
    public void Validate_Should_Fail_When_VaccinationReminderDaysBefore_OutOfRange()
    {
        var cmd = new UpdateReminderSettingsCommand(true, 24, true, 31, true);
        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(UpdateReminderSettingsCommand.VaccinationReminderDaysBefore));
    }
}
